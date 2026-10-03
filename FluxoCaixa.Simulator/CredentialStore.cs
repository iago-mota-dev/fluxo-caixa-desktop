using System.Security.Cryptography;
using System.Text;

namespace FluxoCaixa.Simulator;

public sealed class CredentialStore(string directory)
{
 public string Read(string environment)
 {
  var path=Path.Combine(directory,$"{environment}.credential");
  return File.Exists(path)?Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path),null,DataProtectionScope.CurrentUser)):"";
 }
 public void Save(string environment,string token)
 {
  Directory.CreateDirectory(directory);
  File.WriteAllBytes(Path.Combine(directory,$"{environment}.credential"),ProtectedData.Protect(Encoding.UTF8.GetBytes(token),null,DataProtectionScope.CurrentUser));
 }
}
