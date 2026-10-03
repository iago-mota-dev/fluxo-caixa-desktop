> **Tenant obrigatório (nova versão):** enviar `X-Tenant-Id: TENANT_EXEMPLO` em todos os GET, POST, PUT e DELETE, junto da autenticação. O payload permanece igual. Dados existentes serão atribuídos automaticamente ao tenant original pela migration 0006. Veja [Tenants](TENANTS.md). Esta alteração está validada localmente e aguarda ativação em produção.

# Contrato da API: POST original e edicoes versionadas

Atualizado em 03/10/2026. Este e o contrato atual publicado, apos a restauracao do POST original. Substitui a documentacao inicial v2 que exigia Id e Versao em todos os itens do POST.

O POST de sincronizacao mantem o payload original. GUID e versao nao sao exigidos nesse POST: o servidor atribui a identidade e devolve os metadados. PUT e DELETE usam o UUID e a versao observados no GET.

## POST /api/fechamentos

Autenticacao existente e Content-Type: application/json. Android usa Authorization: Bearer ACCESS_TOKEN; desktop usa X-Desktop-Token. Nunca combinar os dois.

```json
[
  {"Data":"2026-10-03T18:00:00-03:00","FormaPagamento":"pix","Valor":100}
]
```

Array de 1 a 6 formas unicas do mesmo dia. Formas: credito, debito, pix, dinheiro, vale refeicao e delivery. Valor em reais, numero JSON nao negativo com ate duas casas, armazenado em centavos INTEGER. Data ISO-8601; enviar offset explicito. O dia e extraido da string, sem converter para UTC.

- Novo conteudo: servidor cria UUID permanente e versao 1.
- Mesma data, forma e valor ativo: servidor reutiliza o registro existente; nao altera seus dados ou versao.
- Mesma data/forma com valor diferente: cria outro UUID e preserva ambos os registros ativos.
- O GET devolve ambos os lancamentos separadamente e Total soma os valores de todos os ativos. Nao deduplicar por forma no aplicativo.
- POST nao edita ou exclui registros anteriores. Para corrigir um registro, usar PUT.
- Uma exclusao existente da mesma data/forma bloqueia novas criacoes por POST original, com 409, para nao recriar silenciosamente dados excluidos por um envio atrasado. Repeticoes de operacoes ja confirmadas retornam a confirmacao anterior, sem recriar.
- Todo lote e auditoria sao atomicos. Um item recusado desfaz o lote. O total do dia tambem deve respeitar o limite de centavos representavel com seguranca.

### Resposta do POST

O retorno nao e um array solto: e um objeto de confirmacao com o array FormasPagamento. Ha um objeto nesse array para cada item enviado, na mesma ordem. O servidor devolve Id e Versao para o aplicativo guardar e usar nas edicoes/exclusoes.

Exemplo HTTP 200 para o item pix acima:

```json
{
  "Sucesso": true,
  "DataCaixa": "2026-10-03",
  "Quantidade": 1,
  "FormasPagamento": [
    {
      "Id": "90aa33a0-6c1b-4f29-8e30-a2edebcd1101",
      "Versao": 1,
      "Data": "2026-10-03T18:00:00-03:00",
      "FormaPagamento": "pix",
      "Valor": 100,
      "AtualizadoEm": "2026-10-03T21:00:01.000Z",
      "AlteradoEmCliente": null,
      "ExcluidoEm": null
    }
  ]
}
```

Para dois itens enviados, Quantidade e 2 e FormasPagamento tem dois objetos. A resposta traz somente os itens confirmados desse POST, nao todos os lancamentos que existem no dia; para obter o conjunto completo, usar GET.

O ID e gerado pelo servidor quando cria um registro. Se reconhecer um conteudo ativo ja existente, retorna os metadados desse registro, sem incrementar a versao. Como PUT/DELETE podem ter sido executados depois de um POST, essa versao pode ser maior que 1. Nao assumir que cada POST cria um novo ID ou incrementa a versao.

Em retry de uma operacao anteriormente confirmada, o servidor devolve a confirmacao original. Nesse caso os metadados representam o resultado daquela operacao e podem estar desatualizados; consultar GET para obter o estado atual.

AlteradoEmCliente e apenas auditoria e pode ser null no POST original. AtualizadoEm e ExcluidoEm sao definidos pelo servidor. O aplicativo envia somente Data, FormaPagamento e Valor e armazena os metadados recebidos.

### Exemplo de dois valores preservados

1. POST envia pix de 03/10 com Valor 100: servidor devolve ID A.
2. Outro POST envia pix de 03/10 com Valor 200: servidor devolve ID B.
3. Os dois registros permanecem ativos; o GET apresenta ambos e Total soma 300.
4. Repetir o conteudo 200 reutiliza o registro/confirmacao e nao cria um terceiro.
5. Para corrigir A, usar PUT /api/fechamentos/{IdA} com sua versao observada. Para excluir A, usar DELETE por esse ID e versao.


## Idempotencia

Idempotency-Key UUID e opcional no POST original; recomenda-se persistir a chave e corpo antes de enviar. Sem esse header, o servidor deriva uma chave do conteudo e usuario. Retries do mesmo lote nao duplicam registros. O mesmo valor ativo e reconhecido mesmo quando o timestamp do caixa mudou dentro do dia.

PUT e DELETE continuam exigindo Idempotency-Key. Repetir mesmos bytes, metodo e URL com a mesma chave; nao recalcular timestamps. Operacoes novas exigem outras chaves. Reusar chave com outro conteudo/autor retorna 409. Confirmacoes antigas nao representam necessariamente o estado atual: consultar GET novamente.

O POST com Id e Versao explicitos continua aceito como extensao versionada para clientes que ja implementaram esse fluxo; nessa extensao a chave e obrigatoria, versao zero cria e versao positiva atualiza somente se ainda for atual. Essa extensao nao e necessaria para a sincronizacao original.

## GET

GET /api/fechamentos?dataInicio=YYYY-MM-DD&dataFim=YYYY-MM-DD: intervalo inclusivo, inicio e fim juntos. limit padrao 30, maximo 90 dias com registros. Sem filtro, dias recentes. A lista omite excluidos e pode devolver multiplos objetos da mesma forma de pagamento, cada um com ID proprio. A interface deve permitir selecionar pelo ID, nao so pela forma.

GET /api/fechamentos/{Id}: consulta individual, inclusive tombstone. UUID desconhecido retorna 404. Mudanca de data ou forma nao muda o UUID.

## PUT /api/fechamentos/{Id}

Autenticacao, application/json e Idempotency-Key obrigatorios.

```json
{
  "Versao":1,
  "Data":"2026-10-04T18:00:00-03:00",
  "FormaPagamento":"dinheiro",
  "Valor":250.50,
  "AlteradoEmCliente":"2026-10-03T21:05:00.000Z"
}
```

Usar a versao observada quando a intencao foi criada. Retorna mesmo Id e versao incrementada. Se houver outro ativo com a mesma data, forma E valor no destino, retorna 409 sem sobrescrever. Valores diferentes podem coexistir no mesmo dia/forma.

## DELETE /api/fechamentos/{Id}

Autenticacao, application/json e Idempotency-Key obrigatorios; recebe corpo JSON:

```json
{"Versao":2,"AlteradoEmCliente":"2026-10-03T21:10:00.000Z"}
```

Retorna Sucesso, Id, Versao incrementada, Excluido true e ExcluidoEm. Mantem o registro como tombstone. Versoes antigas ou novas operacoes sobre tombstone retornam 409. Retry com a chave original retorna a confirmacao original.

## Conflitos e relogios

PUT, DELETE e POST versionado com base antiga retornam 409. Nao renovar a versao automaticamente. Preservar a intencao, consultar por UUID, comparar e exigir aprovacao para criar outra operacao sobre a versao consultada. Dois usuarios editando a mesma versao nao podem ambos sobrescreve-la.

O POST original nao recebe a versao observada; portanto nao detecta qual das duas intencoes representa a correcao mais recente. Em vez de sobrescrever, preserva valores diferentes como registros separados. Essa preservacao e distinta da prioridade por versao usada no PUT/DELETE.

AlteradoEmCliente e opcional, apenas auditoria, com offset/Z e ate tres casas de fracao. AtualizadoEm e ExcluidoEm sao UTC do servidor. Relogio do celular/desktop nao determina prioridade.

400: payload invalido. 401: autenticacao invalida. 409: conflito ou protecao de excluidos. 428: falta de chave em gravacoes versionadas ou rota antiga PUT/DELETE por data/forma. 500/503/rede: repetir operacao persistida. Erro habitual: {"Sucesso":false,"Erro":"mensagem"}.

## Migracao, auditoria e desktop

Registros anteriores receberam UUID e versao 1 na migration 0004. A migration 0005 permite valores diferentes na mesma data/forma e preserva todos os dados atuais. Auditorias antigas continuam armazenadas. lancamento_auditoria registra novos estados e sincronizacao_operacoes registra confirmacoes. Nao apagar tombstones/chaves sem definir politica de retencao e limite offline.

O desktop envia somente Data, FormaPagamento e Valor no POST, e guarda os IDs/versoes devolvidos. Sua fila persiste a chave de operacao e usa o mesmo corpo nos retries. A interface identifica multiplos lancamentos pela forma, valor e ID. PUT/DELETE permanecem versionados. Filas antigas e recusas que exigem intervencao sao revisadas explicitamente.
