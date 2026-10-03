> **Tenant obrigatório:** configure `FLUXO_TENANT_ID` ou preencha Tenant e clique em Aplicar tenant. Todas as requisições incluem `X-Tenant-Id`. Veja [Tenants](TENANTS.md). Backend com tenant ainda aguarda ativação em produção.

# Fluxo de Caixa - simulador desktop

Aplicacao Windows Forms .NET 10 para fechamento com fila persistida, consulta, edicao e exclusao via API externa. O POST mantem Data, FormaPagamento e Valor, sem GUID/versao exigidos. O servidor gera a identidade. PUT/DELETE usam UUID permanente e controle de versao. Consulte [Contrato atual da API](CONTRATO_API.md), incluindo migração, JSON de todas as rotas e conflitos.

## Executar

Requer Windows e SDK .NET 10 para compilar. O executavel publicado e autocontido.

```powershell
powershell -ExecutionPolicy Bypass -File ./Start-Simulator.ps1 -Build
```

O modo Local usa http://127.0.0.1:8787; inicie seu backend local separadamente. Para o modo remoto, configure FLUXO_API_BASE_URL no ambiente antes de abrir. Sem configuracao, o programa mostra somente https://api.example.invalid. Este repositorio nao distribui enderecos reais de producao. O backend deve implementar o contrato atual: POST original sem metadados obrigatorios, com PUT/DELETE versionados.

## Credenciais

Nenhum token real e distribuido. Obtenha o token com o responsavel pela API. Informe o token original (nao o hash) pela interface e salve, ou importe um arquivo local. Credenciais sao protegidas por DPAPI CurrentUser. Nunca publique credenciais nem dados de caixa.

Opcionalmente configure FLUXO_DESKTOP_TOKEN_LOCAL e FLUXO_DESKTOP_TOKEN_PRODUCTION no ambiente antes de executar. .env.example tem somente placeholders; o aplicativo nao carrega .env automaticamente. FLUXO_SIMULATOR_DATA_DIR permite escolher outro diretorio; o padrao e %LOCALAPPDATA%/FluxoCaixaSimulator, separado por ambiente.

## Contrato de sincronizacao

No POST, enviar um array contendo somente Data, FormaPagamento e Valor. O retorno e um objeto com Sucesso, DataCaixa, Quantidade e FormasPagamento; cada item confirmado inclui Id e Versao gerados/reutilizados pelo servidor. Guardar esses metadados para PUT/DELETE. Consulte os exemplos completos no [contrato atual](CONTRATO_API.md).

O POST nao sobrescreve um valor diferente ja salvo na mesma data/forma: preserva os dois registros. O GET devolve ambos e o total soma os valores. Para corrigir um registro especifico, usar PUT pelo ID e versao.

## Operacoes

- Fechar e enviar: persiste payload original e chave opcional de operacao; guarda UUIDs/versoes devolvidos pelo servidor. Valores diferentes na mesma data/forma sao preservados separadamente e somados no total.
- Salvar sem enviar: salva Pending e pausa envio automatico.
- Reenviar pendencias: repete payload e chave originais.
- Consultar dia: carrega o estado remoto sem renovar pendencias.
- Editar/excluir: usa UUID e versao consultados antes do dialogo; conflito nao sobrescreve. A lista distingue registros pela forma, valor e ID.
- Revisar conflito / fila antiga: compara estado remoto e intencao local. Somente confirmacao explicita cria nova revisao com outra chave.

Filas antigas aparecem como NeedsReview. 409 pausa retries manuais e automaticos. O simulador usa arquivo com escrita atomica; a aplicacao operacional deve salvar fechamento e outbox na mesma transacao do banco local. Edicao e exclusao da interface sao online; as filas automaticas sao de lotes de fechamento.

## Testes

```powershell
dotnet run --project FluxoCaixa.Tests -c Release
```

Teste integrado opcional exclusivamente com backend loopback:

```powershell
dotnet run --project FluxoCaixa.Tests -c Release -- --integration http://127.0.0.1:8787 CAMINHO_DO_TOKEN_LOCAL
```

Testes cobrem UUID/operacao persistidos, retries, conflito pausado, fila antiga, PUT/DELETE versionados, respostas antigas e confirmacao incompleta. O teste integrado tambem verifica o POST original com dois valores preservados e a exclusao protegida por versao. Tokens unitarios sao ficticios.

FluxoCaixa.Core contem contrato, persistencia e HTTP. FluxoCaixa.Simulator contem interface e DPAPI. FluxoCaixa.Tests e um executavel de testes sem framework externo.
