// ActivitySource and Meter listeners are PROCESS-WIDE: a listener registered by one test observes every
// other test running at the same time. The observability assertions ("exactly one span", "exactly one
// measurement") are therefore only meaningful when nothing else is in flight.
//
// The whole suite runs in well under a second, so serialising it costs nothing and removes a class of
// failure that presents as a flake and is really a measurement error.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
