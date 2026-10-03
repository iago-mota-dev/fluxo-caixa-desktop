using System.Globalization;
using System.Text.Json.Serialization;

namespace FluxoCaixa.Core;

public record Grupo(
 [property: JsonPropertyName("FormaPagamento")] string FormaPagamento,
 [property: JsonPropertyName("Valor")] decimal Valor,
 [property: JsonPropertyName("Data")] string Data);
public record FormaTotal(string FormaPagamento, decimal Valor);
public record Confirmacao(bool Sucesso, string DataCaixa, int Quantidade, FormaTotal[] FormasPagamento);
public record DiaTotal(string Data, decimal Total, FormaTotal[] FormasPagamento);
public record OutboxEntry(string Day, int Revision, Grupo[] Payload, string Status = "Pending",
 int Attempts = 0, string? LastError = null, DateTimeOffset? NextAttempt = null,
 DateTimeOffset? SentAt = null, bool RequiresAttention = false);

public static class Contrato
{
 public static string ProductionUrl => Environment.GetEnvironmentVariable("FLUXO_API_BASE_URL") ?? "https://api.example.invalid";
 public const string LocalUrl = "http://127.0.0.1:8787";
 public static readonly string[] Formas = ["credito", "debito", "pix", "dinheiro", "vale refeicao", "delivery"];
 public static void Validate(Grupo[] payload)
 {
  if (payload.Length is < 1 or > 6) throw new ArgumentException("Envie de 1 a 6 grupos.");
  var seen = new HashSet<string>();
  string? day = null;
  foreach (var item in payload)
  {
   if (!Formas.Contains(item.FormaPagamento) || !seen.Add(item.FormaPagamento)) throw new ArgumentException("Forma de pagamento inválida ou repetida.");
   if (item.Valor < 0 || decimal.Round(item.Valor, 2) != item.Valor || item.Valor > 15011998757901.65m) throw new ArgumentException("Valor inválido: use reais não negativos e até duas casas decimais.");
   if (!DateTimeOffset.TryParseExact(item.Data, "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) throw new ArgumentException("Data deve conter hora e offset explícito.");
   var current = time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
   day ??= current;
   if (day != current) throw new ArgumentException("Todos os grupos devem ser do mesmo dia.");
  }
 }
}
