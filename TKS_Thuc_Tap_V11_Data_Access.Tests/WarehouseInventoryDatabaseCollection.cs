using Xunit;

namespace TKS_Thuc_Tap_V11_Data_Access.Tests;

/* These integration tests share queue and projection tables in one local SQL
   database.  Serial execution prevents test cleanup from contending with a
   different test's worker claim. */
[CollectionDefinition("Warehouse inventory database", DisableParallelization = true)]
public sealed class WarehouseInventoryDatabaseCollection
{
}
