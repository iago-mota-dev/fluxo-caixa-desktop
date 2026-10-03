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
   foreach(var entry in store.Snapshot().Where(x=>x.Status=="Pending" && (manual || (!x.RequiresAttention && (x.NextAttempt==null || x.NextAttempt<=DateTimeOffset.UtcNow)))).OrderBy(x=>x.Day))
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
     request.Headers.Add("X-Desktop-Token",token); request.Content=JsonContent.Create(entry.Payload);
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
      !result.FormasPagamento.Select(x=>(x.FormaPagamento,x.Valor)).SequenceEqual(entry.Payload.Select(x=>(x.FormaPagamento,x.Valor))))
      throw new InvalidDataException("Resposta 200 não confirmou o lote enviado.");
     store.UpdateResult(entry.Day,entry.Revision,true,null,false,TimeSpan.Zero); sent++;
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
 public async Task<string> ChangeAsync(string day,string form,Grupo? replacement,CancellationToken cancellation=default)
 {
  await gate.WaitAsync(cancellation);
  try
  {
   if(store.Snapshot().Any(e=>e.Status=="Pending" && (e.Day==day || e.Day==replacement?.Data[..10])))
    throw new InvalidOperationException("Sincronize as pendências dos dias envolvidos antes de editar ou excluir.");
   if(replacement is not null)Contrato.Validate([replacement]);
   var token=getToken().Trim();
   if(token.Length<32)throw new InvalidOperationException("Configure a credencial deste ambiente.");
   using var request=new HttpRequestMessage(replacement is null?HttpMethod.Delete:HttpMethod.Put,
    new Uri(baseUri,$"/api/fechamentos/{Uri.EscapeDataString(day)}/{Uri.EscapeDataString(form)}"));
   request.Headers.Add("X-Desktop-Token",token);
   if(replacement is not null)request.Content=JsonContent.Create(replacement);
   using var result=await http.SendAsync(request,cancellation);
   var json=await result.Content.ReadAsStringAsync(cancellation);
   if(!result.IsSuccessStatusCode)throw new HttpRequestException($"Alteração recusada: HTTP {(int)result.StatusCode}. {json.Replace(token,"[credencial]")}");
   using var doc=JsonDocument.Parse(json);
   if(!doc.RootElement.GetProperty("Sucesso").GetBoolean())throw new InvalidDataException("Alteração não confirmada.");
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
  using var doc=JsonDocument.Parse(json);
  return JsonSerializer.Serialize(doc.RootElement,new JsonSerializerOptions{WriteIndented=true});
 }
}
