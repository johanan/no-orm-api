namespace todo_api
#nowarn "20"
open System
open System.Collections.Generic
open System.Data
open System.Threading.Tasks
open Dapper
open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Npgsql

module Models =
    type [<CLIMutable>] Todo = {
        Id: Guid
        Description: string
        Completed: bool
        Created: DateTimeOffset
        User: string
    }
    
    type [<CLIMutable>] CreateTodo = {
        Description: string
    }

open Models

type HttpError = NotFound | BadRequest of string | Unauthorized
    
module BusinessRules =
    let completeTodo todo =
        match todo with
        | { Completed = true } -> Error (BadRequest "Todo already completed")
        | _ -> Ok { todo with Completed = true }
        
    let toggleTodo todo = { todo with Completed = not todo.Completed }

module NoORM = 
    let runDs (ds: NpgsqlDataSource) query =
        task {
            use! db = ds.OpenConnectionAsync()
            let! result = query db
            return result
        }

    let runTrx (ds: NpgsqlDataSource) query =
        task {
            use! db = ds.OpenConnectionAsync()
            use! trx = db.BeginTransactionAsync()
            do! query trx
            do! trx.CommitAsync()
            return ()
        }
        
    let migration (db: NpgsqlConnection) =
        db.ExecuteAsync("
        CREATE TABLE IF NOT EXISTS Users (
            UserId int PRIMARY KEY GENERATED ALWAYS AS IDENTITY,
            Email TEXT NOT NULL UNIQUE
        );
        CREATE TABLE IF NOT EXISTS Todo (
            Id UUID PRIMARY KEY,
            Description TEXT NOT NULL,
            Completed BOOLEAN NOT NULL,
            Created TIMESTAMPTZ NOT NULL,
            LastModified TIMESTAMPTZ NOT NULL,
            UserId int NOT NULL
        );
        ")
    
    let getTodos (email: string) (db: IDbConnection) =
        let p = DynamicParameters()
        p.Add("Email", email.ToLowerInvariant())
        db.QueryAsync<Todo>("SELECT t.*, u.email as User FROM Todo t JOIN USERS u ON u.userid = t.userid WHERE Completed = false AND u.email = @Email", p)
    
    let getTodo (email: string) id (db: NpgsqlConnection) =
        task {
            let p = DynamicParameters()
            p.Add("Id", id)
            p.Add("Email", email.ToLowerInvariant())
            let! result = db.QueryFirstOrDefaultAsync<Todo>("SELECT t.*, u.email as User FROM Todo t JOIN Users u on t.UserId = u.UserId WHERE t.Id = @Id and u.email = @Email", p)
            if obj.Equals(result, null) then return None else return Some result
        }
        
    let updateTodo todo (db: NpgsqlConnection) =
        task {
            let p = DynamicParameters()
            p.Add("Id", todo.Id)
            p.Add("Description", todo.Description)
            p.Add("Completed", todo.Completed)
            p.Add("LastModified", DateTimeOffset.UtcNow, DbType.DateTimeOffset)
            let! _ = db.ExecuteAsync("UPDATE Todo SET Description = @Description, Completed = @Completed, LastModified = @LastModified WHERE Id = @Id", p)
            return ()
        }
        
    let upsertUser (email: string) (trx: NpgsqlTransaction) =
        task {
            let p = DynamicParameters()
            p.Add("Email", email.ToLowerInvariant())
            let! _ = trx.Connection.ExecuteAsync("INSERT INTO Users (Email) VALUES (@Email) ON CONFLICT DO NOTHING", p, trx)
            return ()
        }
        
    let createTodo todo (trx: NpgsqlTransaction) =
        task {
            let p = DynamicParameters()
            p.Add("Id", todo.Id)
            p.Add("Description", todo.Description)
            p.Add("Completed", todo.Completed)
            p.Add("Created", todo.Created)
            p.Add("Email", todo.User.ToLowerInvariant())
            let! _ = trx.Connection.ExecuteAsync("
                INSERT INTO Todo (Id, Description, Completed, Created, LastModified, UserId)
                VALUES (@Id, @Description, @Completed, @Created, @Created, (SELECT UserId FROM Users WHERE Email = @Email))", p, trx)
            return ()
        }
        
    let insertTodo todo (trx: NpgsqlTransaction) =
        task {
            do! upsertUser todo.User trx
            do! createTodo todo trx
            return ()
        }
    
    type MigrationService(ds: NpgsqlDataSource) =
        interface IHostedService with
            member _.StartAsync(cancellationToken: Threading.CancellationToken) =
                task {
                    printfn "Starting migration"
                    let! _ = runDs ds migration
                    return ()
                }
            member _.StopAsync(cancellationToken: Threading.CancellationToken) = Task.CompletedTask

module Utils =
    let handleError err =
        match err with
        | NotFound -> Results.NotFound()
        | BadRequest m -> Results.BadRequest({| message = m |})
        | Unauthorized -> Results.Unauthorized()
    let handleOk result = Results.Ok(result)
    let handle tr = tr |> TaskResult.foldResult handleOk handleError
    let validUser email = if String.IsNullOrWhiteSpace email then Error Unauthorized else Ok email

module Program =
    open NoORM
    open BusinessRules
    open Utils
    let exitCode = 0

    type public Startup = class end
    [<EntryPoint>]
    let main args =
        let builder = WebApplication.CreateBuilder(args)
        builder.Services.AddHostedService<MigrationService>()
        builder.Services.AddSingleton<NpgsqlDataSource>(fun sp ->
            let cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
            NpgsqlDataSource.Create(cs)
        )
        
        let app = builder.Build()
        app.UseHttpsRedirection()

        app.MapGet("/todos/{email}", Func<NpgsqlDataSource, string, Task<IEnumerable<Todo>>>(fun (ds: NpgsqlDataSource) email -> runDs ds (getTodos email)))
        
        app.MapPost("/todos/{email}", Func<NpgsqlDataSource, string, CreateTodo, Task<IResult>>(fun (ds: NpgsqlDataSource) email (todo: CreateTodo) ->
            taskResult {
                let! validDescription = if String.IsNullOrWhiteSpace(todo.Description) then Error (BadRequest "Description is required ") else Result.Ok todo.Description 
                let todo = { Id = Guid.NewGuid(); Description = validDescription; Completed = false; Created = DateTimeOffset.UtcNow; User = email }
                do! runTrx ds (insertTodo todo)
                return todo
            } |> handle
        ))
        
        app.MapPost("/todos/{email}/{id}/complete", Func<NpgsqlDataSource, string, Guid, Task<IResult>>(fun (ds: NpgsqlDataSource) email id ->
            taskResult {
                let! _ = validUser email
                let! todo = runDs ds (getTodo email id) |> TaskResult.requireSome NotFound
                let! completed = completeTodo todo
                do! runDs ds (updateTodo completed)
                return completed
            } |> handle
        ))
        
        app.MapPost("/todos/{email}/{id}/toggle", Func<NpgsqlDataSource, string, Guid, Task<IResult>>(fun (ds: NpgsqlDataSource) email id ->
            taskResult {
                let! _ = validUser email
                let! todo = runDs ds (getTodo email id) |> TaskResult.requireSome NotFound
                let toggled = toggleTodo todo
                do! runDs ds (updateTodo toggled)
                return toggled
            } |> handle
        ))
        app.Run()
        exitCode