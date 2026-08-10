using Xunit;

// Integration test classes each provision their own database on one shared SQL Server
// instance. Running the collections in parallel means several CREATE DATABASE operations
// racing while other connections are logging in, which surfaces as an intermittent
// "Cannot open database ... login failed" that has nothing to do with the code under test.
//
// The suite is fast enough that serialising it costs little, and a test suite that fails
// occasionally for reasons unrelated to the change being tested is worse than a slower one.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
