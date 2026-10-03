# Contrato v2: identidade, concorrência e sincronização

Este contrato substitui a gravação por UPSERT sem versão. Datas e formas continuam sendo campos editáveis de negócio. Cada lançamento possui um UUID permanente. A combinação de data do caixa e forma de pagamento é única entre lançamentos ativos.

## Autenticação

Android: `Authorization: Bearer ACCESS_TOKEN` emitido pelo Auth0 para a audience da API. Desktop: `X-Desktop-Token`. Nunca enviar os dois headers juntos. Nenhuma credencial D1 é enviada aos aplicativos.

## Migração dos dados existentes

A migration `0004_versioned_fechamentos.sql` atribui UUID e `Versao: 1` aos registros existentes, preservando valores em centavos, data/hora, data do caixa, forma e timestamp existente. Os IDs são gerados uma única vez no banco. Clientes devem consultar os dados novamente para obter identidade e versão; não gerar novos IDs para registros já existentes. As auditorias antigas são preservadas.

## Regra de concorrência

Uma escrita só é aceita quando a versão observada pelo aplicativo ainda é a versão atual do lançamento ativo. O servidor incrementa a versão, dentro da mesma transação da escrita e da auditoria. A exclusão também incrementa a versão e marca `ExcluidoEm`. Não há sobrescrita baseada no relógio dos dispositivos.

`AtualizadoEm` e `ExcluidoEm` são timestamps UTC atribuídos pelo servidor. `AlteradoEmCliente` é uma informação opcional de auditoria, no formato ISO-8601 com offset ou Z e até três casas de fração de segundo. Ela não determina prioridade. `Data` continua sendo a data/hora do caixa e pode mudar, sem alterar o UUID. O dia é extraído da string, sem converter para UTC.

## Idempotência obrigatória

Todo POST, PUT e DELETE deve enviar `Idempotency-Key: UUID_DA_OPERACAO`. Gerar a chave antes de persistir uma operação na fila. Reenviar exatamente o mesmo método, URL e corpo serializado com a mesma chave; não atualizar o timestamp nem a versão no retry.

O servidor persiste a confirmação na mesma transação dos dados. Repetições da operação original retornam essa confirmação, sem nova escrita ou auditoria, mesmo quando o lançamento já mudou depois. A confirmação representa aquela operação; consultar GET para conhecer o estado atual. Usar a mesma chave com outro conteúdo ou outro usuário/origem retorna 409. Uma nova decisão do usuário exige nova chave.

## Consultar

`GET /api/fechamentos?dataInicio=YYYY-MM-DD&dataFim=YYYY-MM-DD` mantém o intervalo inclusivo. Sem filtro, retorna dias recentes. `limit`: padrão 30, máximo 90 dias com registros. A lista omite excluídos. Cada forma agora traz:

```json
{
  "Id": "90aa33a0-6c1b-4f29-8e30-a2edebcd1101",
  "Versao": 1,
  "Data": "2026-10-03T18:00:00-03:00",
  "FormaPagamento": "pix",
  "Valor": 100,
  "AtualizadoEm": "2026-10-03T21:00:00.000Z",
  "AlteradoEmCliente": null,
  "ExcluidoEm": null
}
```

`GET /api/fechamentos/{Id}` retorna o objeto individual, inclusive tombstones. Um tombstone conserva a última data, forma e valor, com versão nova e `ExcluidoEm` preenchido. A consulta por UUID permite distinguir exclusão de mudança de data. UUID desconhecido retorna 404.

## Criar e sincronizar um lote

`POST /api/fechamentos`, `Content-Type: application/json`, autenticação e `Idempotency-Key`.

```json
[
  {
    "Id": "90aa33a0-6c1b-4f29-8e30-a2edebcd1101",
    "Versao": 0,
    "Data": "2026-10-03T18:00:00-03:00",
    "FormaPagamento": "pix",
    "Valor": 100,
    "AlteradoEmCliente": "2026-10-03T21:00:00.000Z"
  }
]
```

Para criação, o aplicativo gera e persiste o UUID antes do primeiro envio; `Versao: 0` significa que esse ID ainda não existe. O servidor confirma `Versao: 1`. Para sincronizar alterações de um registro existente, usar o mesmo UUID e a versão observada, nunca zero. UUIDs excluídos não podem ser reutilizados nem restaurados por POST. Uma criação deliberada posterior pode usar outro UUID, se a combinação data/forma estiver livre.

Mantém-se o array de 1 a 6 formas únicas do mesmo dia. Valor é número JSON em reais, não negativo, até duas casas decimais; armazenado INTEGER em centavos. O lote inteiro e a auditoria são atômicos: um único item com versão antiga, UUID excluído ou destino ocupado desfaz todos os itens. Omissões não excluem formas.

A resposta 200 tem `Sucesso`, `DataCaixa`, `Quantidade` e `FormasPagamento`, com os IDs e versões confirmadas. Persistir as versões retornadas somente na revisão local que originou o envio. Uma resposta antiga não confirma uma revisão local nova.

## Editar

`PUT /api/fechamentos/{Id}` recebe todos os campos de negócio novos e a versão original:

```json
{
  "Versao": 1,
  "Data": "2026-10-04T18:00:00-03:00",
  "FormaPagamento": "dinheiro",
  "Valor": 250.50,
  "AlteradoEmCliente": "2026-10-03T21:05:00.000Z"
}
```

O UUID da URL permanece constante. A resposta confirma o mesmo `Id`, `Versao: 2`, campos novos e `AtualizadoEm`. Destino ativo ocupado retorna 409 sem alterar ambos os registros.

## Excluir

`DELETE /api/fechamentos/{Id}` também exige JSON, `Content-Type: application/json`, autenticação e chave de operação:

```json
{
  "Versao": 2,
  "AlteradoEmCliente": "2026-10-03T21:10:00.000Z"
}
```

Retorna 200 com `Sucesso`, `Id`, `Versao: 3`, `Excluido: true`, `ExcluidoEm` e `AtualizadoEm`. O registro permanece no banco como tombstone. Retry com a chave original retorna a confirmação original. Outra operação sobre o tombstone ou versão antiga retorna 409; não pode apagar um registro novo com outro UUID na mesma data/forma.

## Conflitos e falhas

- 409: versão antiga, UUID excluído, destino ocupado ou uso incorreto da chave de operação. Preservar intenção local, pausar reenvios e consultar estado atual. Nunca renovar a versão automaticamente para tentar vencer o conflito.
- 428: ausência da chave de operação ou uso da antiga rota por data/forma. Atualizar o cliente.
- 400: JSON, ID, versão, data ou valor inválidos.
- 401: renovar a autenticação ou reprovisionar o token.
- 500/503/rede/timeout: resultado pode ser desconhecido. Repetir a operação persistida com a mesma chave e mesmo corpo. Não criar nova operação cegamente.

Formato de erro habitual: `{"Sucesso":false,"Erro":"mensagem"}`. A rede ou borda pode retornar outro formato.

## Desktop e fila antiga

O simulador persiste UUIDs, versões e chave de operação com cada revisão da outbox. Fila antiga sem metadados aparece como `NeedsReview` e não é enviada. 409 mantém `Pending` com atenção, sem retry automático nem manual cego. Consultar a API não muda a versão de uma pendência.

O botão **Revisar conflito / fila antiga** mostra estado remoto e intenção local. Somente após confirmação explícita cria uma revisão nova com as versões consultadas e outra chave de operação. A revisão pode ser recusada novamente se houver mudança concorrente. Caso o usuário aprove recriar uma forma excluída, a criação recebe outro UUID.

As ações de editar/excluir da interface são online: usam a versão carregada antes do diálogo, não buscam uma versão nova imediatamente antes de enviar. Se a resposta se perder, consultar o servidor antes de executar outra ação. A fila automática é destinada aos lotes de fechamento.

## Auditoria e limite da regra

`lancamento_auditoria` registra operação, UUID, versão anterior/nova, estado anterior/novo, autor, origem, timestamp do cliente e recebimento do servidor. `sincronizacao_operacoes` conserva confirmações de idempotência. Tombstones e chaves de operação não devem ser apagados sem definir um limite de retenção/offline e mudar o contrato.

A regra protege escritas baseadas em versões antigas. Um usuário que consulte uma versão nova e aprove explicitamente outra alteração pode atualizá-la. Um dispositivo que nunca observou um registro existente deve consultar/revisar, não fazer criação por data/forma na esperança de sobrescrever.
