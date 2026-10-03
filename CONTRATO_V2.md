# Contrato v2 - referencia anterior

A documentacao atual foi movida para [CONTRATO_API.md](CONTRATO_API.md).

O POST mantem Data, FormaPagamento e Valor, sem GUID ou versao obrigatorios no corpo. O servidor gera os metadados e retorna os objetos em FormasPagamento. Valores diferentes na mesma data/forma coexistem; valores iguais nao duplicam reenvios. PUT e DELETE continuam usando UUID e versao.

Consulte o contrato atual para exemplos completos, idempotencia, exclusao logica e tratamento de conflitos.
