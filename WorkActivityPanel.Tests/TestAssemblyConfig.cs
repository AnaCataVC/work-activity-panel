using Xunit;

// Disable parallel test execution across all test classes to prevent race conditions on shared static helpers
[assembly: CollectionBehavior(DisableTestParallelization = true)]
