namespace FluxoCaixa.Simulator;
internal static class Program
{
 [STAThread]
 static void Main(string[] args)
 {
  using var mutex=new Mutex(true,args.Length>0 && args[0]=="--preview"?"FluxoCaixaSimulator.Preview":"FluxoCaixaSimulator.Desktop",out var created);
  if(!created){MessageBox.Show("O simulador já está aberto.");return;}
  ApplicationConfiguration.Initialize();
  System.Globalization.CultureInfo.CurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
  try
  {
   if(args.Length==2 && args[0]=="--preview")
   {
    using var form=new MainForm();form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-3000,-3000);
    form.Show();Application.DoEvents();
    using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));
    bitmap.Save(Path.GetFullPath(args[1]),System.Drawing.Imaging.ImageFormat.Png);return;
   }
   Application.Run(new MainForm());
  }
  catch(Exception){MessageBox.Show("Não foi possível abrir os dados locais. Preserve os arquivos e consulte desktop/README.md.","Simulador",MessageBoxButtons.OK,MessageBoxIcon.Error);}
 }
}
