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
 private readonly TabControl tabs=new(){Dock=DockStyle.Fill};
 private readonly DateTimePicker periodStart=new(){Format=DateTimePickerFormat.Short,Width=135,Value=DateTime.Today.AddDays(-30)};
 private readonly DateTimePicker periodEnd=new(){Format=DateTimePickerFormat.Short,Width=135};
 private readonly DataGridView records=new(){Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,RowHeadersVisible=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false,BackgroundColor=Color.White};
 private readonly Label summary=new(){AutoSize=true,Text="Escolha o periodo e clique em Consultar periodo."};
 private readonly TextBox details=new(){Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Font=new Font("Consolas",10)};
 private readonly Label context=new(){AutoSize=true,ForeColor=Color.FromArgb(17,52,75)};
 private readonly List<Button> buttons=[];
 private readonly System.Windows.Forms.Timer retry=new(){Interval=45000};
 private OutboxStore store=null!;
 private SyncService sync=null!;
 private CredentialStore credentials=null!;
 private bool busy;
 private bool switching;
 private int appliedEnvironment;
 private string EnvironmentKey=>environment.SelectedIndex==1?"production":"local";
 public MainForm()
 {
  Text="Fluxo de Caixa | Simulador desktop";Width=1160;Height=900;MinimumSize=new Size(1040,800);StartPosition=FormStartPosition.CenterScreen;
  Font=new Font("Segoe UI",10);BackColor=Color.FromArgb(244,247,250);credentials=new CredentialStore(directory);
  var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=3};
  root.RowStyles.Add(new RowStyle(SizeType.Absolute,110));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,45));Controls.Add(root);
  var heading=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};
  heading.Controls.Add(new Label{Text="Fluxo de caixa",AutoSize=true,Font=new Font("Segoe UI",22,FontStyle.Bold),ForeColor=Color.FromArgb(17,52,75)});heading.Controls.Add(context);root.Controls.Add(heading,0,0);root.Controls.Add(tabs,0,1);root.Controls.Add(status,0,2);
  var closing=new TabPage("Fechamento e sincronizacao"){BackColor=BackColor,Padding=new Padding(14)};
  var consulting=new TabPage("Consulta e edicao"){BackColor=BackColor,Padding=new Padding(14)};
  var settings=new TabPage("Configuracao"){BackColor=BackColor,Padding=new Padding(14)};tabs.TabPages.AddRange([closing,consulting,settings]);
  var closeRoot=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5};
  foreach(var height in new[]{60,110,110})closeRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,height));closeRoot.RowStyles.Add(new RowStyle(SizeType.Percent,65));closeRoot.RowStyles.Add(new RowStyle(SizeType.Percent,35));closing.Controls.Add(closeRoot);
  var moment=new FlowLayoutPanel{Dock=DockStyle.Fill};moment.Controls.Add(new Label{Text="Fechamento:",AutoSize=true,Padding=new Padding(0,7,0,0)});moment.Controls.Add(date);moment.Controls.Add(time);moment.Controls.Add(new Label{Text="Fuso UTC:",AutoSize=true,Padding=new Padding(10,7,0,0)});moment.Controls.Add(offset);moment.Controls.Add(total);closeRoot.Controls.Add(moment,0,0);
  var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=6,RowCount=2};grid.RowStyles.Add(new RowStyle(SizeType.Absolute,40));grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));
  foreach(var form in Contrato.Formas){grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/6));var column=values.Count;grid.Controls.Add(new Label{Text=form,AutoSize=true,Font=new Font("Segoe UI",10,FontStyle.Bold),Padding=new Padding(0,10,0,0)},column,0);var amount=new NumericUpDown{DecimalPlaces=2,Minimum=0,Maximum=1000000000,ThousandsSeparator=true,Dock=DockStyle.Top,Increment=10};amount.ValueChanged+=(_,_)=>UpdateTotal();values.Add(form,amount);grid.Controls.Add(amount,column,1);}closeRoot.Controls.Add(grid,0,1);
  var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};actions.Controls.Add(Button("Fechar e enviar",async()=>await CloseAsync()));actions.Controls.Add(Button("Salvar no computador",()=>SaveOnly()));actions.Controls.Add(Button("Reenviar pendencias",async()=>await SendAsync(true)));actions.Controls.Add(Button("Revisar pendencia do dia",async()=>await ReviewAsync()));actions.SetFlowBreak(actions.Controls[^1],true);actions.Controls.Add(automatic);closeRoot.Controls.Add(actions,0,2);
  var local=new GroupBox{Text="Fila de sincronizacao deste tenant",Dock=DockStyle.Fill,Padding=new Padding(10)};local.Controls.Add(history);closeRoot.Controls.Add(local,0,3);
  var payload=new GroupBox{Text="Ultimo fechamento / retorno de sincronizacao",Dock=DockStyle.Fill,Padding=new Padding(10)};payload.Controls.Add(response);closeRoot.Controls.Add(payload,0,4);
  var queryRoot=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5};queryRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,60));queryRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,60));queryRoot.RowStyles.Add(new RowStyle(SizeType.Absolute,38));queryRoot.RowStyles.Add(new RowStyle(SizeType.Percent,65));queryRoot.RowStyles.Add(new RowStyle(SizeType.Percent,35));consulting.Controls.Add(queryRoot);
  var filters=new FlowLayoutPanel{Dock=DockStyle.Fill};filters.Controls.Add(new Label{Text="De:",AutoSize=true,Padding=new Padding(0,7,0,0)});filters.Controls.Add(periodStart);filters.Controls.Add(new Label{Text="Ate:",AutoSize=true,Padding=new Padding(10,7,0,0)});filters.Controls.Add(periodEnd);filters.Controls.Add(Button("Consultar periodo",async()=>await QueryAsync()));queryRoot.Controls.Add(filters,0,0);
  var editActions=new FlowLayoutPanel{Dock=DockStyle.Fill};editActions.Controls.Add(Button("Editar selecionado",async()=>await ChangeAsync(false)));editActions.Controls.Add(Button("Excluir selecionado",async()=>await ChangeAsync(true)));editActions.Controls.Add(new Label{Text="Selecione um lancamento na tabela abaixo.",AutoSize=true,Padding=new Padding(10,10,0,0)});queryRoot.Controls.Add(editActions,0,1);queryRoot.Controls.Add(summary,0,2);
  foreach(var column in new[]{"Data / hora","Forma de pagamento","Valor","Versao","ID"})records.Columns.Add(column,column);
  records.Columns[2].DefaultCellStyle.Format="C2";records.Columns[0].FillWeight=130;records.Columns[4].FillWeight=190;records.SelectionChanged+=(_,_)=>ShowSelection();queryRoot.Controls.Add(records,0,3);
  var detailBox=new GroupBox{Text="Detalhes do lancamento selecionado",Dock=DockStyle.Fill,Padding=new Padding(10)};detailBox.Controls.Add(details);queryRoot.Controls.Add(detailBox,0,4);
  environment.Items.AddRange(["Local (valores de teste)","API publicada (banco remoto)"]);tenantInput.Text=System.Environment.GetEnvironmentVariable("FLUXO_TENANT_ID")??"";
  var config=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true};settings.Controls.Add(config);
  config.Controls.Add(new Label{Text="Conexao e credencial",AutoSize=true,Font=new Font("Segoe UI",16,FontStyle.Bold)});
  var configLine=new FlowLayoutPanel{AutoSize=true};configLine.Controls.Add(environment);configLine.Controls.Add(endpoint);config.Controls.Add(configLine);
  var tenantLine=new FlowLayoutPanel{AutoSize=true};tenantLine.Controls.Add(new Label{Text="Tenant ID:",AutoSize=true,Padding=new Padding(0,7,0,0)});tenantLine.Controls.Add(tenantInput);tenantLine.Controls.Add(Button("Aplicar tenant",()=>SwitchEnvironment()));config.Controls.Add(tenantLine);config.Controls.Add(notice);
  var auth=new FlowLayoutPanel{AutoSize=true};auth.Controls.Add(new Label{Text="Token desktop:",AutoSize=true,Padding=new Padding(0,7,0,0)});auth.Controls.Add(token);auth.Controls.Add(Button("Salvar credencial",()=>SaveCredential()));auth.Controls.Add(Button("Importar arquivo",()=>ImportCredential()));config.Controls.Add(auth);
  config.Controls.Add(new Label{Text="Fila e cache separados por ambiente e tenant. Credencial protegida neste usuario Windows.",AutoSize=true,Padding=new Padding(0,15,0,0)});
  environment.SelectedIndexChanged+=(_,_)=>{if(switching)return;try{SwitchEnvironment();}catch(Exception e){switching=true;environment.SelectedIndex=appliedEnvironment;switching=false;ShowError(e);}};environment.SelectedIndex=0;
  retry.Tick+=async(_,_)=>{if(automatic.Checked && !busy && activeTenant.Length>0 && tenantInput.Text.Trim()==activeTenant && token.Text.Trim().Length>=32)await SendAsync(false);};
  Shown+=async(_,_)=>{retry.Start();if(automatic.Checked && activeTenant.Length>0 && tenantInput.Text.Trim()==activeTenant && token.Text.Trim().Length>=32)await SendAsync(false);};
  FormClosing+=(_,e)=>{if(busy){e.Cancel=true;status.Text="Aguarde concluir o envio (timeout de 20 segundos).";}else retry.Stop();};
  UpdateTotal();
 }
 internal void SelectPreviewTab(int index)=>tabs.SelectedIndex=index;
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
  var requested=tenantInput.Text.Trim();
  if(requested.Length>0 && !System.Text.RegularExpressions.Regex.IsMatch(requested,"^[A-Za-z0-9_-]{1,128}$"))throw new ArgumentException("Tenant invalido: use letras, numeros, _ ou -, ate 128 caracteres.");
  endpoint.Text=environment.SelectedIndex==1?Contrato.ProductionUrl:Contrato.LocalUrl;
  notice.Text=environment.SelectedIndex==1?"Envios neste modo alteram o banco remoto. Use fechamentos autorizados.":"Modo local: inicie o Worker em 127.0.0.1:8787 para testar.";
  notice.ForeColor=environment.SelectedIndex==1?Color.DarkRed:Color.DarkSlateGray;

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
  response.Clear();records.Rows.Clear();details.Clear();summary.Text="Escolha o periodo e clique em Consultar periodo.";context.Text=$"{environment.Text} | Tenant: {(activeTenant.Length==0?"nao configurado":activeTenant[..Math.Min(16,activeTenant.Length)]+"...")}";appliedEnvironment=environment.SelectedIndex;RefreshHistory();
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
  try{await LoadPeriodAsync();status.Text="Consulta completa. Selecione um lancamento para ver os detalhes ou editar.";}
  catch{records.Rows.Clear();details.Clear();summary.Text="Consulta nao concluida; tente novamente.";throw;}
  finally{SetBusy(false);}
 }
 private async Task LoadPeriodAsync()
 {
  var days=await sync.QueryPeriodAsync(periodStart.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),periodEnd.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));
  records.Rows.Clear();details.Clear();
  foreach(var day in days)foreach(var item in day.FormasPagamento){var i=records.Rows.Add(DateTimeOffset.Parse(item.Data,CultureInfo.InvariantCulture).ToString("dd/MM/yyyy HH:mm:ss"),item.FormaPagamento,item.Valor,item.Versao,item.Id);records.Rows[i].Tag=new Grupo(item.FormaPagamento,item.Valor,item.Data,item.Id,item.Versao);}
  summary.Text=$"{days.Length} dias com dados | {records.Rows.Count} lancamentos | Total do periodo: {days.Sum(d=>d.Total):C}";
  if(records.Rows.Count>0){records.CurrentCell=records.Rows[0].Cells[0];records.Rows[0].Selected=true;}ShowSelection();
 }
 private void ShowSelection()
 {
  var selected=records.CurrentRow?.Tag as Grupo;
  details.Text=selected is null?"Nenhum lancamento selecionado.":$"ID: {selected.Id}\r\nVersao: {selected.Versao}\r\nData original: {selected.Data}\r\nForma: {selected.FormaPagamento}\r\nValor: {selected.Valor:C}";
 }
 private async Task ChangeAsync(bool delete)
 {
  if(busy)return;CheckTenant();
  var selected=records.CurrentRow?.Tag as Grupo ?? throw new InvalidOperationException("Consulte o periodo e selecione um lancamento.");
  var sourceDay=selected.Data[..10];
  using var dialog=new Form{Text=delete?"Excluir lancamento":"Editar lancamento",Width=560,Height=510,StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false};
  var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(15),FlowDirection=FlowDirection.TopDown,WrapContents=false};dialog.Controls.Add(panel);
  panel.Controls.Add(new Label{Text=$"Dia original: {sourceDay}. Alteracao usa a versao consultada.",AutoSize=true});
  panel.Controls.Add(new Label{Text=$"{selected.FormaPagamento} | {selected.Valor:C} | versao {selected.Versao}",AutoSize=true});
  panel.Controls.Add(new Label{Text=$"ID: {selected.Id}",AutoSize=true});
  var destination=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=390};destination.Items.AddRange(Contrato.Formas);
  var newDate=new DateTimePicker{Format=DateTimePickerFormat.Custom,CustomFormat="dd/MM/yyyy HH:mm:ss",Width=390};
  var amount=new NumericUpDown{DecimalPlaces=2,Maximum=15011998757901.65m,ThousandsSeparator=true,Width=390};
  var originalTime=DateTimeOffset.Parse(selected.Data,CultureInfo.InvariantCulture);
  var editOffset=new NumericUpDown{Minimum=-14,Maximum=14,DecimalPlaces=2,Increment=0.25m,Width=390,Value=(decimal)originalTime.Offset.TotalHours};
  destination.SelectedItem=selected.FormaPagamento;amount.Value=selected.Valor;newDate.Value=originalTime.DateTime;
  if(!delete){panel.Controls.Add(new Label{Text="Forma, data/hora, valor e fuso UTC:",AutoSize=true});panel.Controls.Add(destination);panel.Controls.Add(newDate);panel.Controls.Add(amount);panel.Controls.Add(editOffset);}
  panel.Controls.Add(new Label{Text=$"Ambiente: {environment.Text}",AutoSize=true,ForeColor=Color.DarkRed});
  var confirm=new Button{Text=delete?"Confirmar exclusao":"Confirmar edicao",AutoSize=true,DialogResult=DialogResult.OK};panel.Controls.Add(confirm);dialog.AcceptButton=confirm;
  if(dialog.ShowDialog(this)!=DialogResult.OK)return;
  Grupo? replacement=null;
  if(!delete){var instant=new DateTimeOffset(DateTime.SpecifyKind(newDate.Value,DateTimeKind.Unspecified),TimeSpan.FromHours((double)editOffset.Value));replacement=new Grupo(destination.Text,amount.Value,instant.ToString("yyyy-MM-dd'T'HH:mm:sszzz",CultureInfo.InvariantCulture));}
  SetBusy(true);
  try{
   var confirmation=await sync.ChangeAsync(selected,replacement);RefreshHistory();records.Rows.Clear();details.Clear();
   try{await LoadPeriodAsync();status.Text=delete?"Exclusao confirmada; periodo atualizado.":"Edicao confirmada; periodo atualizado.";}
   catch(Exception e){summary.Text="Alteracao confirmada; consulte novamente para atualizar a lista.";status.Text=summary.Text;MessageBox.Show(this,status.Text+"\r\n"+e.Message,"Consulta",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
  }
  catch{records.Rows.Clear();details.Clear();summary.Text="Consulte novamente antes de repetir a alteracao.";throw;}
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
 private void SetBusy(bool value){busy=value;foreach(var b in buttons)b.Enabled=!value;environment.Enabled=!value;token.Enabled=!value;tenantInput.Enabled=!value;periodStart.Enabled=!value;periodEnd.Enabled=!value;date.Enabled=!value;time.Enabled=!value;offset.Enabled=!value;foreach(var amount in values.Values)amount.Enabled=!value;automatic.Enabled=!value;UseWaitCursor=value;}
 private void ShowError(Exception e){status.Text=e is IOException?"Falha ao salvar/ler dados locais. Não considere o caixa fechado até corrigir.":e.Message.Replace(token.Text.Length>0?token.Text:"__no_token__","[credencial]");MessageBox.Show(this,status.Text,"Simulador",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
 protected override void Dispose(bool disposing){if(disposing){retry.Dispose();http.Dispose();}base.Dispose(disposing);}
}
