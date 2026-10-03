# Tenants: integração obrigatória

Funcionalidade publicada em 03/10/2026. Migration 0006 aplicada no D1 remoto e isolamento verificado na API publicada.

Todas as chamadas GET, POST, PUT e DELETE devem incluir o cabeçalho:

```http
X-Tenant-Id: TENANT_EXEMPLO
```

O identificador é uma string de 1 a 128 caracteres: letras ASCII, números, hífen ou sublinhado. Maiúsculas e minúsculas distinguem tenants. Ausência ou formato inválido retorna HTTP 400 após a autenticação. Não existe fallback para o tenant original.

A autenticação permanece obrigatória: Authorization: Bearer ACCESS_TOKEN no Android ou X-Desktop-Token no desktop. O tenant é um seletor de agrupamento, não uma senha nem uma autorização adicional. Conforme o acesso atual da aplicação, um usuário autenticado pode selecionar outro tenant; não há controle de associação usuário/tenant.

## Dados existentes e migração

O ID original foi gerado uma única vez como hash SHA-256 de bytes aleatórios. Está no arquivo privado `.tenant-original` da instalação do backend; não é publicado no repositório desktop. Distribua esse ID aos clientes que devem acessar os dados atuais.

A migration versionada `0006_tenants.sql` cria o tenant original e atribui automaticamente a ele todos os fechamentos existentes, inclusive excluídos, auditorias e operações de sincronização. IDs, versões, valores e respostas históricas são preservados. Nenhuma edição manual dos registros é necessária.

A migration deve preceder o deploy do Worker. Os clientes devem receber o ID antes da ativação: depois dela, requisições sem o cabeçalho serão recusadas. A migration foi validada localmente e posteriormente aplicada no D1 remoto, com autorização e backup. Todos os registros existentes foram preservados.

## Isolamento e sincronização

Consultas, totais, deduplicação do POST, exclusões, auditoria e Idempotency-Key são separados por tenant. A mesma chave pode ser usada em tenants diferentes sem reaproveitar a resposta do outro tenant. GET por UUID de outro tenant retorna 404; PUT/DELETE não modificam o registro de outro tenant.

Um novo ID válido é cadastrado automaticamente na primeira gravação bem-sucedida. Consultar um tenant sem dados retorna um array vazio. POST continua recebendo o array original com Data, FormaPagamento e Valor; não adicionar tenant ao payload. IDs e versões continuam gerados internamente e retornados pela API.

## Android

Configure o ID do tenant e envie o cabeçalho em todas as operações. Separe cache, fila offline e metadados Id/Versao por tenant. Uma operação pendente mantém o tenant com que foi criada, inclusive nos retries; trocar o tenant da interface nunca deve redirecionar pendências antigas. PUT/DELETE usam o UUID e versão consultados no mesmo tenant.

## Desktop de exemplo

Configure `FLUXO_TENANT_ID` ou informe o campo Tenant e clique em Aplicar tenant. Sem tenant aplicado, fechar, consultar e sincronizar ficam impedidos. O transporte envia o cabeçalho em todas as chamadas.

A fila e o cache local são separados por ambiente e por hash do ID do tenant. Trocar o tenant não transporta pendências de outro. Para migrar automaticamente a fila anterior, configure também `FLUXO_ORIGINAL_TENANT_ID` com o ID original: os arquivos antigos são copiados somente para esse tenant quando o destino ainda não existe; os arquivos anteriores são preservados. O launcher privado já fornece os dois valores a partir de `.tenant-original`. O launcher público usa apenas a configuração fornecida pelo integrador.
