using FluxoCaixa.Core;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

var root=Path.Combine(Path.GetTempPath(),"fluxo-simulator-tests-"+Guid.NewGuid());
Directory.CreateDirectory(root);
int passed=0;
void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
Grupo[] Payload(decimal value=10m)=>Contrato.Formas.Select(f=>new Grupo(f,f=="pix"?value:0,"2026-10-03T23:30:00-03:00")).ToArray();
HttpResponseMessage Ok(Grupo[] p)=>new(HttpStatusCode.OK){Content=JsonContent.Create(new Confirmacao(true,p[0].Data[..10],p.Length,p.Select(x=>new FormaTotal(x.FormaPagamento,x.Valor)).ToArray()))};
async Task Run(string name,Func<Task> test){await test();passed++;Console.WriteLine("PASS "+name);}
try
{
 await Run("editar todos os campos e excluir com URL codificada",async()=>{
  var store=new OutboxStore(Path.Combine(root,"changes.json"));var entry=store.Close(Payload());
  using var http=new HttpClient(new Stub(async req=>{
   Assert(req.Headers.Contains("X-Desktop-Token"),"autenticacao obrigatoria");
   Assert(req.RequestUri!.AbsoluteUri.Contains("vale%20refeicao"),"forma codificada");
   if(req.Method==HttpMethod.Put){var body=await req.Content!.ReadFromJsonAsync<Grupo>();Assert(body!.FormaPagamento=="delivery" && body.Data.StartsWith("2026-10-04") && body.Valor==77.29m,"todos os campos");}
   return new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new Dictionary<string,bool>{{"Sucesso",true}})};
  }));
  var sync=new SyncService(http,store,new Uri("http://localhost"),()=>new string('a',64));
  try{await sync.ChangeAsync(entry.Day,"vale refeicao",null);throw new Exception("pendente deve bloquear");}catch(InvalidOperationException){}
  store.UpdateResult(entry.Day,entry.Revision,true,null,false,TimeSpan.Zero);
  await sync.ChangeAsync(entry.Day,"vale refeicao",new Grupo("delivery",77.29m,"2026-10-04T12:00:00-03:00"));
  Assert(store.Snapshot().Single().Status=="RemoteChanged","historico invalidado sem reenviar");
  await sync.ChangeAsync(entry.Day,"vale refeicao",null);
 });
 await Run("payload decimal, capitalização e validação de lote",()=>{
  var json=JsonSerializer.Serialize(Payload(1450.75m));using var doc=JsonDocument.Parse(json);
  Assert(doc.RootElement.ValueKind==JsonValueKind.Array,"array obrigatório");
  Assert(doc.RootElement[2].GetProperty("Valor").GetDecimal()==1450.75m,"valor em reais");
  Assert(doc.RootElement[2].GetProperty("FormaPagamento").GetString()=="pix","nome público");
  foreach(var invalid in new[]{Payload(-1),Payload(10.999m),new[]{Payload()[0],Payload()[0]},Array.Empty<Grupo>()})
  {try{Contrato.Validate(invalid);throw new Exception("validação deveria recusar");}catch(ArgumentException){}}
  return Task.CompletedTask;
 });
 await Run("fechamento e Pending sobrevivem à reinicialização",()=>{
  var path=Path.Combine(root,"persist.json");var store=new OutboxStore(path);store.Close(Payload());
  var restored=new OutboxStore(path).Snapshot().Single();Assert(restored.Status=="Pending" && restored.Payload.Length==6,"outbox durável");
  Assert(restored.Payload[0].Data[..10]=="2026-10-03","calendário local preservado");return Task.CompletedTask;
 });
 await Run("falha de rede preserva Pending e retry confirma sem duplicar",async()=>{
  var store=new OutboxStore(Path.Combine(root,"retry.json"));store.Close(Payload());int calls=0;
  using var http=new HttpClient(new Stub(async req=>{
   Assert(req.Headers.GetValues("X-Desktop-Token").Single()==new string('a',64),"credencial");
   var payload=await req.Content!.ReadFromJsonAsync<Grupo[]>();
   Assert(payload!.Length==6,"todos os grupos enviados");
   if(calls++==0)throw new HttpRequestException("offline");return Ok(payload);
  }));var sync=new SyncService(http,store,new Uri("http://localhost"),()=>new string('a',64));
  await sync.SyncPendingAsync(true);Assert(store.Snapshot().Single().Status=="Pending","falha não confirma");
  Assert(new OutboxStore(store.FilePath).Snapshot().Single().Attempts==1,"tentativa persistida");
  await sync.SyncPendingAsync(true);Assert(store.Snapshot().Single().Status=="Sent","retry confirmado");
  Assert(store.Snapshot().Length==1 && calls==2,"fechamento único");
 });
 await Run("401 pausa retries automáticos até intervenção",async()=>{
  var store=new OutboxStore(Path.Combine(root,"401.json"));store.Close(Payload());int calls=0;
  using var http=new HttpClient(new Stub(_=>{calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized){Content=JsonContent.Create(new{Erro="Credencial inválida"})});}));
  var sync=new SyncService(http,store,new Uri("http://localhost"),()=>new string('a',64));await sync.SyncPendingAsync(true);await sync.SyncPendingAsync();
  Assert(calls==1 && store.Snapshot().Single().RequiresAttention,"não repetir erro de autenticação");
 });
 await Run("resposta antiga não confirma uma revisão nova",async()=>{
  var store=new OutboxStore(Path.Combine(root,"version.json"));store.Close(Payload(10));
  var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  using var http=new HttpClient(new Stub(async req=>{var sent=await req.Content!.ReadFromJsonAsync<Grupo[]>();started.SetResult();await release.Task;return Ok(sent!);}));
  var sync=new SyncService(http,store,new Uri("http://localhost"),()=>new string('a',64));var pending=sync.SyncPendingAsync(true);await started.Task;
  store.Close(Payload(20));release.SetResult();await pending;
  var current=store.Snapshot().Single();Assert(current.Revision==2 && current.Status=="Pending" && current.Payload[2].Valor==20,"revisão nova permanece pendente");
 });
 await Run("HTTP 200 incompleto não marca Sent",async()=>{
  var store=new OutboxStore(Path.Combine(root,"bad-response.json"));store.Close(Payload());
  using var http=new HttpClient(new Stub(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new Dictionary<string,bool>{{"Sucesso",true}})})));
  await new SyncService(http,store,new Uri("http://localhost"),()=>new string('a',64)).SyncPendingAsync(true);
  Assert(store.Snapshot().Single().Status=="Pending","confirmar lote completo");
 });
 if(args.Length>0 && args[0]=="--integration")
 {
  await Run("integração real Worker + D1 local: fechar, enviar, corrigir e consultar",async()=>{
   var token=File.ReadAllText(args[2]).Trim();var baseUri=new Uri(args[1]);
   Assert(baseUri.IsLoopback,"testes com valores fictícios somente em loopback");
   using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(20)};
   var store=new OutboxStore(Path.Combine(root,"real.json"));var sync=new SyncService(http,store,baseUri,()=>token);
   store.Close(Payload(1450.75m));Assert(await sync.SyncPendingAsync(true)==1,"lote real enviado");
   store.Close(Payload(1450.75m));await sync.SyncPendingAsync(true);
   store.Close(Payload(1500));await sync.SyncPendingAsync(true);
   using var result=JsonDocument.Parse(await sync.QueryAsync("2026-10-03"));
   Assert(result.RootElement[0].GetProperty("Total").GetDecimal()==1500m,"total real atualizado");
   Assert(result.RootElement[0].GetProperty("FormasPagamento").GetArrayLength()==6,"6 formas sem duplicar");
   Assert(store.Snapshot().Single().Status=="Sent","confirmado localmente");
   await sync.ChangeAsync("2026-10-03","pix",new Grupo("pix",88.29m,"2026-10-04T12:00:00-03:00"));
   using var moved=JsonDocument.Parse(await sync.QueryAsync("2026-10-04"));
   Assert(moved.RootElement[0].GetProperty("Total").GetDecimal()==88.29m,"edicao real com nova data");
   await sync.ChangeAsync("2026-10-04","pix",null);
   using var removed=JsonDocument.Parse(await sync.QueryAsync("2026-10-04"));
   Assert(removed.RootElement.GetArrayLength()==0,"exclusao real confirmada");
  });
 }
 Console.WriteLine($"{passed} testes aprovados.");
}
finally
{
 var resolved=Path.GetFullPath(root);if(!resolved.StartsWith(Path.Combine(Path.GetTempPath(),"fluxo-simulator-tests-"),StringComparison.OrdinalIgnoreCase))throw new Exception("Caminho de limpeza inválido");
 Directory.Delete(resolved,true);
}
sealed class Stub(Func<HttpRequestMessage,Task<HttpResponseMessage>> handle):HttpMessageHandler
{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)=>handle(request);}

