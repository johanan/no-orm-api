namespace todo_tests

open System
open System.Collections.Generic
open Microsoft.AspNetCore.Mvc.Testing
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Npgsql
open Xunit
open todo_api.Program
open todo_api.NoORM
open todo_tests.DbSeed

type Factory () =
    inherit WebApplicationFactory<Startup>()
    
    override this.CreateHost (builder) =
        builder.ConfigureAppConfiguration(fun ctx builder ->
            builder.AddInMemoryCollection([
                KeyValuePair<string, string>("ConnectionStrings:Postgres", "Server=localhost;Database=todo_test;UserName=todo-user;Password=password123;")
            ]) |> ignore
        ) |> ignore
        
        base.CreateHost(builder)
        
    member this.Truncate () =
        use scope = this.Services.CreateScope()
        let db = scope.ServiceProvider.GetService<NpgsqlDataSource>()
        runDs db dbTruncate |> Async.AwaitTask |> Async.RunSynchronously
        
    static member SeedDb (factory: Factory) =
        use scope = factory.Services.CreateScope()
        let db = scope.ServiceProvider.GetService<NpgsqlDataSource>()
        runDs db dbSeed |> Async.AwaitTask |> Async.RunSynchronously
        
    static member UseDb (factory: Factory) query =
        use scope = factory.Services.CreateScope()
        let ds = scope.ServiceProvider.GetService<NpgsqlDataSource>()
        runDs ds query
        
    static member UseTrx (factory: Factory) query =
        use scope = factory.Services.CreateScope()
        let ds = scope.ServiceProvider.GetService<NpgsqlDataSource>()
        runTrx ds query