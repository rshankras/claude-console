using Xunit;

// Run the suite serially.
//
// Some state this plugin depends on is genuinely process-global: IpcPaths.ProductSlug decides which
// product's IPC tree every path resolves under, and a product declares itself once at plugin load.
// The tests that exercise that necessarily mutate it, and xunit runs collections in PARALLEL by
// default — so a class reading IpcPaths or constructing a BridgeManager could observe another
// class's slug mid-run and fail somewhere entirely unrelated to the change that broke it.
//
// The whole suite is ~160ms, so determinism is close to free here and a heisenbug in a keypad
// plugin's test suite is not.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
