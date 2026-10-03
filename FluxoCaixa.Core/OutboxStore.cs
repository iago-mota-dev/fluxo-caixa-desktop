using System.Text.Json;

namespace FluxoCaixa.Core;

public sealed class OutboxStore
{
 private readonly string path;
 private readonly object gate = new();
 private readonly JsonSerializerOptions options = new() { WriteIndented = true };
 public OutboxStore(string path) { this.path = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(this.path)!); }
 public string FilePath => path;
 private List<OutboxEntry> Load() => File.Exists(path)
  ? JsonSerializer.Deserialize<List<OutboxEntry>>(File.ReadAllText(path)) ?? throw new InvalidDataException("Arquivo local inválido.") : [];
 private void Write(List<OutboxEntry> entries)
 {
  var temp = path + ".tmp";
  using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
  { JsonSerializer.Serialize(stream, entries, options); stream.Flush(true); }
  if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
 }
 public OutboxEntry[] Snapshot() { lock(gate) return Load().Select(e=>e.OperationId is null || e.Payload.Any(p=>!Guid.TryParse(p.Id,out _)) ? e with {Status="NeedsReview",RequiresAttention=true,LastError="Fila antiga sem versao. Consulte o servidor e revise antes de reenviar."} : e).OrderByDescending(x=>x.Day).ToArray(); }
 public Grupo[] Known(string day)
 {
  lock(gate){var file=path+".known.json";var days=File.Exists(file)?JsonSerializer.Deserialize<Dictionary<string,Grupo[]>>(File.ReadAllText(file))!:[];return days.GetValueOrDefault(day)??[];}
 }
 public void Remember(string day,Grupo[] groups)
 {
  lock(gate){var file=path+".known.json";var days=File.Exists(file)?JsonSerializer.Deserialize<Dictionary<string,Grupo[]>>(File.ReadAllText(file))!:[];days[day]=groups;var temp=file+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(days,options));File.Move(temp,file,true);}
 }

 public OutboxEntry Close(Grupo[] payload,bool reviewed=false)
 {
  Contrato.Validate(payload);
  lock(gate)
  {
   var list=Load(); var day=payload[0].Data[..10]; var old=list.Find(x=>x.Day==day);
   var baseline=reviewed?Known(day):old is not null && old.Status!="RemoteChanged"?old.Payload:Known(day);
   if(!reviewed && old is not null && (old.OperationId is null || old.RequiresAttention))throw new InvalidOperationException("Revise o conflito/fila antiga antes de criar outra revisao.");
   var now=DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
   var versioned=payload.Select(p=>{var known=baseline.FirstOrDefault(x=>x.FormaPagamento==p.FormaPagamento);return p with {Id=known?.Id is {Length:>0}?known.Id:Guid.NewGuid().ToString(),Versao=known?.Versao??0,AlteradoEmCliente=now};}).ToArray();
   var entry=new OutboxEntry(day,(old?.Revision??0)+1,versioned,OperationId:Guid.NewGuid().ToString());
   list.RemoveAll(x=>x.Day==day); list.Add(entry); Write(list); return entry;
  }
 }
 public void InvalidateHistory(string day,string? destination)
 {
  lock(gate)
  {
   var list=Load();
   for(int i=0;i<list.Count;i++)if(list[i].Day==day || list[i].Day==destination)
    list[i]=list[i] with {Status="RemoteChanged",LastError="Dados alterados na API. Consulte o dia para ver os valores atuais.",NextAttempt=null};
   Write(list);
  }
 }
 public void UpdateResult(string day,int revision,bool success,string? error,bool attention,TimeSpan delay,FormaTotal[]? confirmed=null)
 {
  lock(gate)
  {
   var list=Load(); var index=list.FindIndex(x=>x.Day==day && x.Revision==revision);
   if(index<0) return; // An old HTTP response must not confirm a newer local revision.
   var old=list[index]; var now=DateTimeOffset.UtcNow;
   list[index]=old with {Status=success?"Sent":"Pending",Attempts=old.Attempts+1,
    LastError=error,RequiresAttention=attention,SentAt=success?now:null,NextAttempt=success?null:now+delay,
    Payload=success && confirmed is not null?old.Payload.Select(p=>p with {Versao=confirmed.Single(r=>r.Id==p.Id).Versao}).ToArray():old.Payload};
   Write(list);
  }
 }
}
