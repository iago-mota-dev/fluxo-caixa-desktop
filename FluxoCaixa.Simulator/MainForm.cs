using FluxoCaixa.Core;
using System.Globalization;

namespace FluxoCaixa.Simulator;

public sealed class MainForm : Form
{
 private readonly string directory=System.Environment.GetEnvironmentVariable("FLUXO_SIMULATOR_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"FluxoCaixaSimulator");
 private readonly HttpClient http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(20)};
 private readonly ComboBox environment=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=230};
 private readonly TextBox tenantInput=new(){Width=540};
 private string activeTenant="";
 private readonly Label endpoint=new(){AutoSize=true};
 private readonly Label notice=new(){AutoSize=true,ForeColor=Color.DarkSlateGray};
 private readonly Label status=new(){AutoSize=true,Text="Pronto para simular. O caixa é salvo localmente antes de enviar."};
 private readonly Label total=new(){AutoSize=true,Font=new Font("Segoe UI",14,FontStyle.Bold)};
 private readonly DateTimePicker date=new(){Format=DateTimePickerFormat.Short,Width=140};
 private readonly DateTimePicker time=new(){Format=DateTimePickerFormat.Custom,CustomFormat="HH:mm:ss",ShowUpDown=true,Width=100};
 private readonly NumericUpDown offset=new(){Minimum=-12,Maximum=14,DecimalPlaces=2,Increment=0.5m,Width=75,Value=-3};
 private readonly TextBox token=new(){UseSystemPasswordChar=true,Width=390};
 private readonly CheckBox automatic=new(){AutoSize=true,Checked=true,Text="Reenviar pendências automaticamente (a cada 45 s)"};
 private readonly Dictionary<string,NumericUpDown> values=[];
 private readonly DataGridView history=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,BackgroundColor=Color.White};
 private readonly TextBox response=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,Font=new Font("Consolas",9),WordWrap=false};
 private readonly List<Button> buttons=[];
 private readonly System.Windows.Forms.Timer retry=new(){Interval=45000};
 private OutboxStore store=null!;
 private SyncService sync=null!;
 private CredentialStore credentials=null!;
 private bool busy;
 private string EnvironmentKey=>environment.SelectedIndex==1?"production":"local";
 public MainForm()
 {
  Text="Fluxo de Caixa | Simulador desktop"; Width=1100;Height=990;MinimumSize=new Size(920,850);StartPosition=FormStartPosition.CenterScreen;
  Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(244,247,250);
  credentials=new CredentialStore(directory);
  var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=8};
  root.RowStyles.Add(new RowStyle(SizeType.Absolute,85)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,150));
  root.RowStyles.Add(new RowStyle(SizeType.Absolute,58)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
  root.RowStyles.Add(new RowStyle(SizeType.Absolute,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,180));
  root.RowStyles.Add(new RowStyle(SizeType.Percent,55));root.RowStyles.Add(new RowStyle(SizeType.Percent,45));
  Controls.Add(root);
  var heading=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};
  heading.Controls.Add(new Label{Text="Simular fechamento do caixa",AutoSize=true,Font=new Font("Segoe UI",20,FontStyle.Bold),ForeColor=Color.FromArgb(17,52,75)});
  heading.Controls.Add(new Label{Text="Informe os totais. Salve no computador e sincronize com a API.",AutoSize=true});root.Controls.Add(heading,0,0);
  environment.Items.AddRange(["Local (valores de teste)","API publicada (banco remoto)"]);
  var config=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};
  var configLine=new FlowLayoutPanel{AutoSize=true};configLine.Controls.Add(environment);configLine.Controls.Add(endpoint);config.Controls.Add(configLine);
  tenantInput.Text=System.Environment.GetEnvironmentVariable("FLUXO_TENANT_ID")??"";
  var tenantLine=new FlowLayoutPanel{AutoSize=true};tenantLine.Controls.Add(new Label{Text="Tenant ID:",AutoSize=true,Padding=new Padding(0,7,0,0)});tenantLine.Controls.Add(tenantInput);tenantLine.Controls.Add(Button("Aplicar tenant",()=>SwitchEnvironment()));config.Controls.Add(tenantLine);config.Controls.Add(notice);root.Controls.Add(config,0,1);
  var auth=new FlowLayoutPanel{Dock=DockStyle.Fill};auth.Controls.Add(new Label{Text="Token desktop:",AutoSize=true,Padding=new Padding(0,7,0,0)});auth.Controls.Add(token);
  auth.Controls.Add(Button("Salvar credencial",()=>SaveCredential()));auth.Controls.Add(Button("Importar arquivo",()=>ImportCredential()));root.Controls.Add(auth,0,2);
  var moment=new FlowLayoutPanel{Dock=DockStyle.Fill};moment.Controls.Add(new Label{Text="Dia / hora:",AutoSize=true,Padding=new Padding(0,6,0,0)});moment.Controls.Add(date);moment.Controls.Add(time);moment.Controls.Add(new Label{Text="Offset UTC (horas):",AutoSize=true,Padding=new Padding(12,6,0,0)});moment.Controls.Add(offset);moment.Controls.Add(total);root.Controls.Add(moment,0,3);
  var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=6,RowCount=2};
  foreach(var form in Contrato.Formas)
  {
   grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/6));var column=values.Count;
   grid.Controls.Add(new Label{Text=form,AutoSize=true,Font=new Font("Segoe UI",10,FontStyle.Bold),Padding=new Padding(0,10,0,0)},column,0);
   var amount=new NumericUpDown{DecimalPlaces=2,Minimum=0,Maximum=1000000000,ThousandsSeparator=true,Dock=DockStyle.Top,Increment=10};
   amount.ValueChanged+=(_,_)=>UpdateTotal();values.Add(form,amount);grid.Controls.Add(amount,column,1);
  }
  root.Controls.Add(grid,0,4);
  var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};
  actions.Controls.Add(Button("Fechar caixa e enviar",async()=>await CloseAsync()));
  actions.Controls.Add(Button("Salvar sem enviar",()=>SaveOnly()));
  actions.Controls.Add(Button("Reenviar pendências",async()=>await SendAsync(true)));
  actions.Controls.Add(Button("Consultar dia na API",async()=>await QueryAsync()));
  actions.Controls.Add(Button("Editar lançamento na API",async()=>await ChangeAsync(false)));
  actions.Controls.Add(Button("Excluir lançamento na API",async()=>await ChangeAsync(true)));
  actions.Controls.Add(Button("Revisar conflito / fila antiga",async()=>await ReviewAsync()));
  actions.Controls.Add(automatic);root.Controls.Add(actions,0,5);
  var local=new GroupBox{Text="Fechamentos locais e fila de sincronização",Dock=DockStyle.Fill};
  var localRoot=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2};localRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,32));localRoot.RowStyles.Add(new RowStyle(SizeType.Percent,100));localRoot.Controls.Add(status);localRoot.Controls.Add(history,0,1);local.Controls.Add(localRoot);root.Controls.Add(local,0,6);
  var remote=new GroupBox{Text="Consulta / payload do último fechamento (sem credencial)",Dock=DockStyle.Fill};remote.Controls.Add(response);root.Controls.Add(remote,0,7);
  environment.SelectedIndexChanged+=(_,_)=>SwitchEnvironment();environment.SelectedIndex=0;
  retry.Tick+=async(_,_)=>{if(automatic.Checked && !busy && activeTenant.Length>0 && tenantInput.Text.Trim()==activeTenant && token.Text.Trim().Length>=32)await SendAsync(false);};
  Shown+=async(_,_)=>{retry.Start();if(automatic.Checked && activeTenant.Length>0 && tenantInput.Text.Trim()==activeTenant && token.Text.Trim().Length>=32)await SendAsync(false);};
  FormClosing+=(_,e)=>{if(busy){e.Cancel=true;status.Text="Aguarde concluir o envio (timeout de 20 segundos).";}else retry.Stop();};
  UpdateTotal();
 }
 private Button Button(string text,Action action)
 {
  var b=new Button{Text=text,AutoSize=true,Padding=new Padding(8,4,8,4),BackColor=Color.White,FlatStyle=FlatStyle.Flat};
  b.Click+=(_,_)=>{try{action();}catch(Exception e){ShowError(e);}};buttons.Add(b);return b;
 }
 private Button Button(string text,Func<Task> action)
 {
  var b=new Button{Text=text,AutoSize=true,Padding=new Padding(8,4,8,4),BackColor=Color.White,FlatStyle=FlatStyle.Flat};
  b.Click+=async(_,_)=>{try{await action();}catch(Exception e){ShowError(e);}};buttons.Add(b);return b;
 }
 private void SwitchEnvironment()
 {
  endpoint.Text=environment.SelectedIndex==1?Contrato.ProductionUrl:Contrato.LocalUrl;
  notice.Text=environment.SelectedIndex==1?"Envios neste modo alteram o banco remoto. Use fechamentos autorizados.":"Modo local: inicie o Worker em 127.0.0.1:8787 para testar.";
  notice.ForeColor=environment.SelectedIndex==1?Color.DarkRed:Color.DarkSlateGray;
  var requested=tenantInput.Text.Trim();
  if(requested.Length>0 && !System.Text.RegularExpressions.Regex.IsMatch(requested,"^[A-Za-z0-9_-]{1,128}$"))throw new ArgumentException("Tenant invalido: use letras, numeros, _ ou -, ate 128 caracteres.");
  activeTenant=requested;
  var bucket=activeTenant.Length==0?"unconfigured":Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(activeTenant))).ToLowerInvariant();
  var scopedPath=Path.Combine(directory,EnvironmentKey,"tenants",bucket,"fechamentos.json");
  var original=System.Environment.GetEnvironmentVariable("FLUXO_ORIGINAL_TENANT_ID");
  var legacyPath=Path.Combine(directory,EnvironmentKey,"fechamentos.json");
  if(activeTenant.Length>0 && activeTenant==original && File.Exists(legacyPath) && !File.Exists(scopedPath)){
   Directory.CreateDirectory(Path.GetDirectoryName(scopedPath)!);File.Copy(legacyPath,scopedPath);
   if(File.Exists(legacyPath+".known.json"))File.Copy(legacyPath+".known.json",scopedPath+".known.json");
  }
  store=new OutboxStore(scopedPath);
  token.Text=credentials.Read(EnvironmentKey);
  var provisioned=System.Environment.GetEnvironmentVariable(EnvironmentKey=="local"?"FLUXO_DESKTOP_TOKEN_LOCAL":"FLUXO_DESKTOP_TOKEN_PRODUCTION");
  if(!string.IsNullOrWhiteSpace(provisioned)){token.Text=provisioned;SaveCredential();}
  sync=activeTenant.Length>0?new SyncService(http,store,new Uri(endpoint.Text),()=>token.Text.Trim(),activeTenant):null!;
  response.Clear();RefreshHistory();
 }
 private void SaveCredential(){if(token.Text.Trim().Length<32)throw new ArgumentException("Informe o token original com pelo menos 32 caracteres.");credentials.Save(EnvironmentKey,token.Text.Trim());status.Text="Credencial protegida para este usuário Windows e ambiente.";}
 private void ImportCredential(){using var dialog=new OpenFileDialog{Title="Selecionar o arquivo de token deste ambiente",Filter="Todos os arquivos|*.*"};if(dialog.ShowDialog(this)==DialogResult.OK){token.Text=File.ReadAllText(dialog.FileName).Trim();SaveCredential();}}
 private Grupo[] Payload()
 {
  var instant=date.Value.Date+time.Value.TimeOfDay;
  var timestamp=new DateTimeOffset(DateTime.SpecifyKind(instant,DateTimeKind.Unspecified),TimeSpan.FromHours((double)offset.Value)).ToString("yyyy-MM-dd'T'HH:mm:sszzz",CultureInfo.InvariantCulture);
  return Contrato.Formas.Select(f=>new Grupo(f,values[f].Value,timestamp)).ToArray();
 }
 private bool ConfirmRemote()=>EnvironmentKey!="production" || MessageBox.Show(this,"Este fechamento será enviado ao banco remoto publicado. Continuar?","Envio para API publicada",MessageBoxButtons.YesNo,MessageBoxIcon.Information)==DialogResult.Yes;
 private void CheckTenant(){if(activeTenant.Length==0 || tenantInput.Text.Trim()!=activeTenant)throw new InvalidOperationException("Informe Tenant ID e clique em Aplicar tenant antes de salvar/enviar.");}
 private void SaveOnly()
 {
  CheckTenant();
  var entry=store.Close(Payload());RefreshHistory();response.Text=System.Text.Json.JsonSerializer.Serialize(entry.Payload,new System.Text.Json.JsonSerializerOptions{WriteIndented=true});status.Text="Fechamento salvo localmente como Pending. Envio automático pausado.";automatic.Checked=false;
 }
 private async Task CloseAsync()
 {
  if(!ConfirmRemote())return;
  CheckTenant();
  var entry=store.Close(Payload());RefreshHistory();response.Text=System.Text.Json.JsonSerializer.Serialize(entry.Payload,new System.Text.Json.JsonSerializerOptions{WriteIndented=true});
  status.Text="Caixa fechado e salvo localmente. Tentando sincronizar...";await SendAsync(true);
 }
 private async Task SendAsync(bool manual)
 {
  if(busy)return;CheckTenant();SetBusy(true);
  try{var sent=await sync.SyncPendingAsync(manual);RefreshHistory();var pending=store.Snapshot().Count(e=>e.Status=="Pending");status.Text=$"Caixa local preservado. Enviados nesta tentativa: {sent}. Pendentes: {pending}.";}
  catch(Exception e){ShowError(e);}finally{SetBusy(false);}
 }
 private async Task QueryAsync()
 {
  if(busy)return;CheckTenant();SetBusy(true);
  try{response.Text=await sync.QueryAsync(date.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));status.Text="Consulta concluída.";}
  finally{SetBusy(false);}
 }
 private async Task ChangeAsync(bool delete)
 {
  if(busy)return;CheckTenant();
  var sourceDay=date.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
  SetBusy(true);
  try{response.Text=await sync.QueryAsync(sourceDay);}finally{SetBusy(false);}
  var current=store.Known(sourceDay);
  if(current.Length==0)throw new InvalidOperationException("Nao ha lancamentos ativos nesse dia.");
  using var dialog=new Form{Text=delete?"Excluir lancamento":"Editar lancamento",Width=460,Height=390,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false};
  var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(15),FlowDirection=FlowDirection.TopDown,WrapContents=false};dialog.Controls.Add(panel);
  panel.Controls.Add(new Label{Text=$"Dia original: {sourceDay}. Alteracao usa a versao consultada.",AutoSize=true});
  var source=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=390};source.Items.AddRange(current.Select(p=>$"{p.FormaPagamento} | {p.Valor:C} | v{p.Versao} | {p.Id[..8]}").ToArray());source.SelectedIndex=0;panel.Controls.Add(source);
  var destination=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=390};destination.Items.AddRange(Contrato.Formas);
  var newDate=new DateTimePicker{Format=DateTimePickerFormat.Custom,CustomFormat="dd/MM/yyyy HH:mm:ss",Width=390};
  var amount=new NumericUpDown{DecimalPlaces=2,Maximum=15011998757901.65m,ThousandsSeparator=true,Width=390};
  void Fill(){var selected=current[source.SelectedIndex];destination.SelectedItem=selected.FormaPagamento;amount.Value=selected.Valor;newDate.Value=DateTimeOffset.Parse(selected.Data,CultureInfo.InvariantCulture).DateTime;}
  source.SelectedIndexChanged+=(_,_)=>Fill();Fill();
  if(!delete){panel.Controls.Add(new Label{Text="Nova forma, data/hora e valor (offset da tela):",AutoSize=true});panel.Controls.Add(destination);panel.Controls.Add(newDate);panel.Controls.Add(amount);}
  panel.Controls.Add(new Label{Text=$"Ambiente: {environment.Text}",AutoSize=true,ForeColor=Color.DarkRed});
  var confirm=new Button{Text=delete?"Confirmar exclusao":"Confirmar edicao",AutoSize=true,DialogResult=DialogResult.OK};panel.Controls.Add(confirm);dialog.AcceptButton=confirm;
  if(dialog.ShowDialog(this)!=DialogResult.OK)return;
  Grupo? replacement=null;
  if(!delete){var instant=new DateTimeOffset(DateTime.SpecifyKind(newDate.Value,DateTimeKind.Unspecified),TimeSpan.FromHours((double)offset.Value));replacement=new Grupo(destination.Text,amount.Value,instant.ToString("yyyy-MM-dd'T'HH:mm:sszzz",CultureInfo.InvariantCulture));}
  SetBusy(true);
  try{response.Text=await sync.ChangeAsync(current[source.SelectedIndex],replacement);RefreshHistory();status.Text=delete?"Exclusao logica confirmada.":"Edicao confirmada. Versao incrementada.";}
  finally{SetBusy(false);}
 }
 private async Task ReviewAsync()
 {
  if(busy)return;CheckTenant();
  var day=date.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
  var pending=store.Snapshot().FirstOrDefault(e=>e.Day==day && (e.Status=="Pending" || e.Status=="NeedsReview"));
  if(pending is null)throw new InvalidOperationException("Nao ha pendencia desse dia para revisar.");
  SetBusy(true);
  try
  {
   var remote=await sync.QueryAsync(day);
   response.Text="ESTADO ATUAL NA API:\r\n"+remote+"\r\n\r\nSUA ALTERACAO PENDENTE:\r\n"+System.Text.Json.JsonSerializer.Serialize(pending.Payload,new System.Text.Json.JsonSerializerOptions{WriteIndented=true});
   using var review=new Form{Text="Comparar conflito",Width=800,Height=650,StartPosition=FormStartPosition.CenterParent};
   var text=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,Dock=DockStyle.Fill,Text=response.Text};
   var approve=new Button{Dock=DockStyle.Bottom,Height=70,Text="Aplicar minha intencao sobre as versoes consultadas (novo envio; pode recriar formas excluidas)",DialogResult=DialogResult.OK};review.Controls.Add(text);review.Controls.Add(approve);
   if(review.ShowDialog(this)!=DialogResult.OK)return;
   store.Close(pending.Payload,reviewed:true);RefreshHistory();automatic.Checked=false;status.Text="Nova revisao aprovada e salva. Clique em Reenviar pendencias para enviar.";
  }
  finally{SetBusy(false);}
 }
 private void RefreshHistory()=>history.DataSource=store.Snapshot().Select(e=>new{Dia=e.Day,Revisão=e.Revision,Total=e.Payload.Sum(x=>x.Valor).ToString("C",CultureInfo.GetCultureInfo("pt-BR")),Status=e.Status,Tentativas=e.Attempts,ÚltimaFalha=e.LastError??"",PróximoEnvio=e.NextAttempt?.ToLocalTime().ToString("HH:mm:ss")??""}).ToArray();
 private void UpdateTotal()=>total.Text="Total: "+values.Values.Sum(v=>v.Value).ToString("C",CultureInfo.GetCultureInfo("pt-BR"));
 private void SetBusy(bool value){busy=value;foreach(var b in buttons)b.Enabled=!value;environment.Enabled=!value;token.Enabled=!value;tenantInput.Enabled=!value;UseWaitCursor=value;}
 private void ShowError(Exception e){status.Text=e is IOException?"Falha ao salvar/ler dados locais. Não considere o caixa fechado até corrigir.":e.Message.Replace(token.Text.Length>0?token.Text:"__no_token__","[credencial]");MessageBox.Show(this,status.Text,"Simulador",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
 protected override void Dispose(bool disposing){if(disposing){retry.Dispose();http.Dispose();}base.Dispose(disposing);}
}
