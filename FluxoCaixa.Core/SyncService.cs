using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FluxoCaixa.Core;

public sealed class SyncService(HttpClient http, OutboxStore store, Uri baseUri, Func<string> getToken)
{
 private readonly SemaphoreSlim gate=new(1,1);
 public async Task<int> SyncPendingAsync(bool manual=false,CancellationToken cancellation=default)
 {
  if(!await gate.WaitAsync(0,cancellation)) return 0;
  int sent=0;
  try
  {
   foreach(var entry in store.Snapshot().Where(x=>x.Status=="Pending" && !x.RequiresAttention && (manual || (!x.RequiresAttention && (x.NextAttempt==null || x.NextAttempt<=DateTimeOffset.UtcNow)))).OrderBy(x=>x.Day))
   {
    cancellation.ThrowIfCancellationRequested();
    if(store.Snapshot().First(x=>x.Day==entry.Day).Revision!=entry.Revision) continue;
    var token=getToken().Trim();
    var delay=TimeSpan.FromSeconds(Math.Min(300,15*Math.Pow(2,Math.Min(entry.Attempts,5))));
    if(token.Length<32)
    {store.UpdateResult(entry.Day,entry.Revision,false,"Configure a credencial deste ambiente.",true,delay); continue;}
    try
    {
     using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(baseUri,"/api/fechamentos"));
     request.Headers.Add("X-Desktop-Token",token); request.Headers.Add("Idempotency-Key",entry.OperationId); request.Content=JsonContent.Create(entry.Payload.Select(p=>new {p.Data,p.FormaPagamento,p.Valor}).ToArray(),options:new JsonSerializerOptions());
     using var response=await http.SendAsync(request,cancellation);
     if(response.StatusCode!=HttpStatusCode.OK)
     {
      bool attention=(int)response.StatusCode is >=400 and <500 && (int)response.StatusCode!=429;
      var message=$"HTTP {(int)response.StatusCode}: envio não confirmado.";
      try {var json=await response.Content.ReadFromJsonAsync<JsonElement>(cancellation); if(json.TryGetProperty("Erro",out var error))message=$"HTTP {(int)response.StatusCode}: {error.GetString()}";} catch(JsonException) {}
      var retry=response.Headers.RetryAfter?.Delta;
      if(response.Headers.RetryAfter?.Date is { } date)retry=date-DateTimeOffset.UtcNow;
      store.UpdateResult(entry.Day,entry.Revision,false,message.Replace(token,"[credencial]"),attention,retry>TimeSpan.Zero?retry.Value:delay);
      continue;
     }
     var result=await response.Content.ReadFromJsonAsync<Confirmacao>(cancellation);
     if(result is null || !result.Sucesso || result.DataCaixa!=entry.Day || result.Quantidade!=entry.Payload.Length || result.FormasPagamento is null || result.FormasPagamento.Length!=entry.Payload.Length ||
      !result.FormasPagamento.Select(x=>(x.FormaPagamento,x.Valor)).SequenceEqual(entry.Payload.Select(x=>(x.FormaPagamento,x.Valor))) || result.FormasPagamento.Any(x=>!Guid.TryParse(x.Id,out _) || x.Versao<1))
      throw new InvalidDataException("Resposta 200 não confirmou o lote enviado.");
     store.UpdateResult(entry.Day,entry.Revision,true,null,false,TimeSpan.Zero,result.FormasPagamento); sent++;
    }
    catch(Exception e) when(e is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
    {
     store.UpdateResult(entry.Day,entry.Revision,false,e is TaskCanceledException?"Tempo limite do envio; será possível reenviar.":"Falha de rede ou confirmação inválida; envio pendente.",false,delay);
    }
   }
   return sent;
  }
  finally{gate.Release();}
 }
 public async Task<string> ChangeAsync(Grupo current,Grupo? replacement,CancellationToken cancellation=default)
 {
  await gate.WaitAsync(cancellation);
  try
  {
   var day=current.Data[..10];
   if(store.Snapshot().Any(e=>(e.Status=="Pending" || e.Status=="NeedsReview") && (e.Day==day || e.Day==replacement?.Data[..10])))
    throw new InvalidOperationException("Sincronize ou revise as pendencias dos dias envolvidos primeiro.");
   if(!Guid.TryParse(current.Id,out _) || current.Versao<1)throw new InvalidOperationException("Consulte o lancamento com Id e Versao antes de alterar.");
   if(replacement is not null)Contrato.Validate([replacement]);
   var token=getToken().Trim();
   if(token.Length<32)throw new InvalidOperationException("Configure a credencial deste ambiente.");
   using var request=new HttpRequestMessage(replacement is null?HttpMethod.Delete:HttpMethod.Put,new Uri(baseUri,$"/api/fechamentos/{current.Id}"));
   request.Headers.Add("X-Desktop-Token",token);
   request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString());
   var changed=DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
   request.Content=replacement is null?JsonContent.Create(new{Versao=current.Versao,AlteradoEmCliente=changed},options:new JsonSerializerOptions())
    :JsonContent.Create(replacement with {Id=current.Id,Versao=current.Versao,AlteradoEmCliente=changed});
   using var result=await http.SendAsync(request,cancellation);
   var json=await result.Content.ReadAsStringAsync(cancellation);
   if(!result.IsSuccessStatusCode)throw new HttpRequestException($"Alteracao recusada: HTTP {(int)result.StatusCode}. {json.Replace(token,"[credencial]")}");
   using var doc=JsonDocument.Parse(json);
   if(!doc.RootElement.GetProperty("Sucesso").GetBoolean() || doc.RootElement.GetProperty("Id").GetString()!=current.Id || doc.RootElement.GetProperty("Versao").GetInt32()!=current.Versao+1)throw new InvalidDataException("Alteracao nao confirmada. Consulte o servidor antes de repetir.");
   store.InvalidateHistory(day,replacement?.Data[..10]);
   return JsonSerializer.Serialize(doc.RootElement,new JsonSerializerOptions{WriteIndented=true});
  }
  finally{gate.Release();}
 }
 public async Task<string> QueryAsync(string day,CancellationToken cancellation=default)
 {
  using var req=new HttpRequestMessage(HttpMethod.Get,new Uri(baseUri,$"/api/fechamentos?dataInicio={day}&dataFim={day}"));
  req.Headers.Add("X-Desktop-Token",getToken());
  using var response=await http.SendAsync(req,cancellation);
  if(!response.IsSuccessStatusCode) throw new HttpRequestException($"Consulta recusada: HTTP {(int)response.StatusCode}.");
  var json=await response.Content.ReadAsStringAsync(cancellation);
  var days=JsonSerializer.Deserialize<DiaTotal[]>(json)??throw new InvalidDataException("Consulta invalida");
  var groups=days.SelectMany(d=>d.FormasPagamento).Select(r=>new Grupo(r.FormaPagamento,r.Valor,r.Data,r.Id,r.Versao)).ToArray();
  if(groups.Any(p=>!Guid.TryParse(p.Id,out _) || p.Versao<1))throw new InvalidDataException("API sem contrato versionado.");
  store.Remember(day,groups);
  using var doc=JsonDocument.Parse(json);
  return JsonSerializer.Serialize(doc.RootElement,new JsonSerializerOptions{WriteIndented=true});
 }
}
