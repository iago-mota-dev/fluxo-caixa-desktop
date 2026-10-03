using FluxoCaixa.Core;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
var root=Path.Combine(Path.GetTempPath(),"fluxo-version-tests-"+Guid.NewGuid());Directory.CreateDirectory(root);int passed=0;
void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
Grupo[] Payload(decimal value=10)=>Contrato.Formas.Select(f=>new Grupo(f,f=="pix"?value:0,"2026-10-03T12:00:00-03:00")).ToArray();
HttpResponseMessage Ok(Grupo[] groups)=>new(HttpStatusCode.OK){Content=JsonContent.Create(new Confirmacao(true,groups[0].Data[..10],groups.Length,groups.Select(g=>new FormaTotal(g.FormaPagamento,g.Valor,Guid.NewGuid().ToString(),1,g.Data)).ToArray()))};
async Task Run(string name,Func<Task> action){await action();passed++;Console.WriteLine("PASS "+name);}
try{
 await Run("tenant obrigatorio impede transporte sem identificacao",()=>{
  var store=new OutboxStore(Path.Combine(root,"empty-tenant.json"));using var http=new HttpClient();
  foreach(var tenant in new[]{"","../outro","tenant com espaco",new string('a',129)}){
   try{_ = new SyncService(http,store,new Uri("http://localhost"),()=>new string('a',64),tenant);throw new Exception("tenant invalido deveria falhar");}catch(ArgumentException){}
  }
  return Task.CompletedTask;
 });
 await Run("UUID e chave de operacao persistem apos reiniciar",()=>{
  var store=new OutboxStore(Path.Combine(root,"persist.json"));var entry=store.Close(Payload());var restarted=new OutboxStore(store.FilePath).Snapshot().Single();
  Assert(restarted.OperationId==entry.OperationId && Guid.TryParse(restarted.OperationId,out _),"chave persistida");
  Assert(restarted.Payload.All(g=>g.Id=="" && g.Versao==0),"ids serao criados somente no servidor");return Task.CompletedTask;
 });
 await Run("retry reutiliza mesmo payload e chave; confirma novas versoes",async()=>{
  var store=new OutboxStore(Path.Combine(root,"retry.json"));store.Close(Payload());string? key=null,json=null;int calls=0;
  using var http=new HttpClient(new Stub(async req=>{
   Assert(req.Headers.GetValues("X-Tenant-Id").Single()=="tenant-test","tenant enviado no POST");
   var currentKey=req.Headers.GetValues("Idempotency-Key").Single();var currentJson=await req.Content!.ReadAsStringAsync();
   using var raw=JsonDocument.Parse(currentJson);Assert(!raw.RootElement[0].TryGetProperty("Id",out _) && !raw.RootElement[0].TryGetProperty("Versao",out _),"POST mantem payload original");
   if(calls++==0){key=currentKey;json=currentJson;throw new HttpRequestException("resposta perdida");}
   Assert(key==currentKey && json==currentJson,"operacao identica no retry");return Ok((await req.Content.ReadFromJsonAsync<Grupo[]>())!);
 }));var sync=new SyncService(http,store,new Uri("http://localhost"),()=>new string('a',64),"tenant-test");
  await sync.SyncPendingAsync(true);Assert(store.Snapshot().Single().Status=="Pending","falha preserva pendencia");
  await sync.SyncPendingAsync(true);var sent=store.Snapshot().Single();Assert(sent.Status=="Sent" && sent.Payload.All(g=>g.Versao==1),"versoes confirmadas");
  var next=store.Close(Payload(99));Assert(next.Payload.Select(g=>g.Id).SequenceEqual(sent.Payload.Select(g=>g.Id)) && next.Payload.All(g=>g.Versao==1),"identidade permanente");
 });
 await Run("409 bloqueia retry manual e automatico sem renovar versao",async()=>{
  var store=new OutboxStore(Path.Combine(root,"conflict.json"));var first=store.Close(Payload());int calls=0;
  using var http=new HttpClient(new Stub(_=>{calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict){Content=JsonContent.Create(new{Erro="versao antiga"})});}));
  var sync=new SyncService(http,store,new Uri("http://localhost"),()=>new string('a',64),"tenant-test");await sync.SyncPendingAsync(true);await sync.SyncPendingAsync(true);await sync.SyncPendingAsync();
  var pending=store.Snapshot().Single();Assert(calls==1 && pending.RequiresAttention && pending.Payload[0].Versao==0,"nao sobrescreve");
  store.Remember(first.Day,first.Payload.Select(g=>g with {Versao=7}).ToArray());Assert(store.Snapshot().Single().Payload[0].Versao==0,"consulta nao modifica pendencia");
  var reviewed=store.Close(pending.Payload,reviewed:true);Assert(reviewed.Payload[0].Versao==7 && reviewed.OperationId!=first.OperationId,"somente revisao explicita adota nova base");
 });
 await Run("fila antiga exige revisao e nao envia",async()=>{
  var file=Path.Combine(root,"legacy.json");File.WriteAllText(file,JsonSerializer.Serialize(new[]{new OutboxEntry("2026-10-03",1,Payload())}));var store=new OutboxStore(file);
  Assert(store.Snapshot().Single().Status=="NeedsReview","migracao segura");int calls=0;
  using var http=new HttpClient(new Stub(_=>{calls++;throw new Exception("nao enviar");}));await new SyncService(http,store,new Uri("http://localhost"),()=>new string('a',64),"tenant-test").SyncPendingAsync(true);Assert(calls==0,"nenhum envio cego");
 });
 await Run("PUT e DELETE usam UUID e versao observada",async()=>{
  var store=new OutboxStore(Path.Combine(root,"changes.json"));var current=new Grupo("pix",10,"2026-10-03T12:00:00-03:00",Guid.NewGuid().ToString(),5);
  using var http=new HttpClient(new Stub(async req=>{
   Assert(req.Headers.GetValues("X-Tenant-Id").Single()=="tenant-test","tenant enviado no PUT/DELETE");
   Assert(req.RequestUri!.AbsolutePath.EndsWith(current.Id),"rota UUID");Assert(Guid.TryParse(req.Headers.GetValues("Idempotency-Key").Single(),out _),"operacao identificada");
   using var body=JsonDocument.Parse(await req.Content!.ReadAsStringAsync());Assert(body.RootElement.GetProperty("Versao").GetInt32()==5,"versao original");
   return new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{Sucesso=true,Id=current.Id,Versao=6},options:new JsonSerializerOptions())};
 }));var sync=new SyncService(http,store,new Uri("http://localhost"),()=>new string('a',64),"tenant-test");
  await sync.ChangeAsync(current,current with {Data="2026-10-04T12:00:00-03:00",FormaPagamento="dinheiro",Valor=99});await sync.ChangeAsync(current,null);
 });
 await Run("resposta antiga nao confirma nova revisao",async()=>{
  var store=new OutboxStore(Path.Combine(root,"race.json"));store.Close(Payload());var started=new TaskCompletionSource();var release=new TaskCompletionSource();
  using var http=new HttpClient(new Stub(async req=>{var p=await req.Content!.ReadFromJsonAsync<Grupo[]>();started.SetResult();await release.Task;return Ok(p!);}));
  var sync=new SyncService(http,store,new Uri("http://localhost"),()=>new string('a',64),"tenant-test");var task=sync.SyncPendingAsync(true);await started.Task;store.Close(Payload(20));release.SetResult();await task;
  Assert(store.Snapshot().Single().Status=="Pending" && store.Snapshot().Single().Revision==2,"revisao nova preservada");
 });
 await Run("confirmacao incompleta nao confirma lote",async()=>{
  var store=new OutboxStore(Path.Combine(root,"invalid.json"));store.Close(Payload());using var http=new HttpClient(new Stub(_=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=JsonContent.Create(new{Sucesso=true})})));
  await new SyncService(http,store,new Uri("http://localhost"),()=>new string('a',64),"tenant-test").SyncPendingAsync(true);Assert(store.Snapshot().Single().Status=="Pending","exige ids e versoes");
 });
 if(args.Length>0 && args[0]=="--integration")await Run("integracao real: POST original preserva registros e DELETE exige versao",async()=>{
  var uri=new Uri(args[1]);Assert(uri.IsLoopback,"somente D1 local");var token=File.ReadAllText(args[2]).Trim();using var http=new HttpClient();var integrationTenant="tenant-test-"+Guid.NewGuid().ToString("N");var store=new OutboxStore(Path.Combine(root,"real.json"));var sync=new SyncService(http,store,uri,()=>token,integrationTenant);
  store.Close(Payload(77.29m));Assert(await sync.SyncPendingAsync(true)==1,"criar");await sync.QueryAsync("2026-10-03");var observed=store.Known("2026-10-03").Single(g=>g.FormaPagamento=="pix");
  await sync.ChangeAsync(observed,observed with {Valor=99});store.Close(Payload(88));Assert(await sync.SyncPendingAsync(true)==1,"POST preserva valor anterior e cria outro registro");
  using var check=JsonDocument.Parse(await sync.QueryAsync("2026-10-03"));Assert(check.RootElement[0].GetProperty("Total").GetDecimal()==187,"dois valores preservados e somados");
  var original=store.Known("2026-10-03").Single(g=>g.Id==observed.Id);Assert(original.Valor==99 && original.Versao==2,"original nao foi sobrescrito");
  try{await sync.ChangeAsync(observed,null);throw new Exception("delete stale deve falhar");}catch(HttpRequestException){}
  await sync.ChangeAsync(original,null);
  await sync.QueryAsync("2026-10-03");Assert(!store.Known("2026-10-03").Any(g=>g.Id==observed.Id),"excluido oculto na lista");
  using var req=new HttpRequestMessage(HttpMethod.Get,new Uri(uri,"/api/fechamentos/"+observed.Id));req.Headers.Add("X-Desktop-Token",token);req.Headers.Add("X-Tenant-Id",integrationTenant);using var resp=await http.SendAsync(req);using var tomb=JsonDocument.Parse(await resp.Content.ReadAsStringAsync());Assert(tomb.RootElement.GetProperty("ExcluidoEm").ValueKind==JsonValueKind.String,"tombstone preservado");
 });
 Console.WriteLine($"{passed} testes aprovados.");
}finally{if(root.StartsWith(Path.Combine(Path.GetTempPath(),"fluxo-version-tests-")))Directory.Delete(root,true);}
sealed class Stub(Func<HttpRequestMessage,Task<HttpResponseMessage>> action):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)=>action(request);}
