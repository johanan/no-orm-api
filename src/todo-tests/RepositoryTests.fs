namespace todo_tests

open System
open Dapper
open Xunit
open todo_api.NoORM
open todo_api.Models

[<Collection("Sequential")>]
type RepositoryTests() =
    let factory = new Factory()
    do Factory.SeedDb factory

    interface IDisposable with
        member this.Dispose() =
            factory.Truncate()
            factory.Dispose()

    [<Fact>]
    member _.``Ensure that only one user is created`` () =
        Factory.UseTrx factory (fun trx ->
            task {
                // exists from seed
                let! count = trx.Connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM Users")
                Assert.Equal(1, count)
                // try to insert it again
                do! upsertUser "existing.user@test.com" trx
                let! count = trx.Connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM Users")
                Assert.Equal(1, count)
                // insert a different user
                do! upsertUser "new.user@test.com" trx
                let! count = trx.Connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM Users")
                Assert.Equal(2, count)
            }
        )
        
    [<Fact>]
    member _.``Ensure create todo creates user on insert or uses existing`` () =
        Factory.UseTrx factory (fun trx ->
            task {
                let todo = { Id = Guid.NewGuid(); Description = "Test"; Completed = false; Created = DateTimeOffset.UtcNow; User = "existing.user@test.com" }
                do! insertTodo todo trx
                let! count = trx.Connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM Users")
                let! todoCount = trx.Connection.QueryAsync<int>("SELECT COUNT(*) FROM Todo")
                let! userId = trx.Connection.QueryFirstOrDefaultAsync<int>("SELECT UserId FROM Users WHERE Email = 'existing.user@test.com'")
                let! todoUserId = trx.Connection.QueryFirstOrDefaultAsync<int>("SELECT UserId FROM Todo WHERE Id = @Id", {| Id = todo.Id |})
                Assert.Equal(1, count)
                Assert.Single(todoCount) |> ignore
                Assert.Equal(userId, todoUserId)
                // add a new todo and new user
                let newUserTodo = { todo with Id = Guid.NewGuid(); User = "different.user@test.com" }
                do! insertTodo newUserTodo trx
                let! count = trx.Connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM Users")
                let! todoCount = trx.Connection.QueryAsync<int>("SELECT COUNT(*) FROM Todo")
                let! userId = trx.Connection.QueryFirstOrDefaultAsync<int>("SELECT UserId FROM Users WHERE Email = @Email", {| Email = newUserTodo.User |})
                let! todoUserId = trx.Connection.QueryFirstOrDefaultAsync<int>("SELECT UserId FROM Todo WHERE Id = @Id", {| Id = newUserTodo.Id |})
                Assert.Equal(2, count)
                Assert.Equal(2, Seq.head todoCount)
                Assert.Equal(userId, todoUserId)
            }
        )
        
    [<Fact>]
    member _.``Ensure getTodos returns only incomplete todos for user`` () =
        Factory.UseTrx factory (fun trx ->
            task {
                // create a few todos for a couple of users
                let todos = [
                    { Id = Guid.NewGuid(); Description = "Test"; Completed = false; Created = DateTimeOffset.UtcNow; User = "existing.user@test.com" }
                    { Id = Guid.NewGuid(); Description = "Test"; Completed = true; Created = DateTimeOffset.UtcNow; User = "existing.user@test.com" }
                    { Id = Guid.NewGuid(); Description = "Test"; Completed = false; Created = DateTimeOffset.UtcNow; User = "existing.user@test.com" }
                    { Id = Guid.NewGuid(); Description = "Test"; Completed = false; Created = DateTimeOffset.UtcNow; User = "new.user@test.com" }
                    { Id = Guid.NewGuid(); Description = "Test"; Completed = true; Created = DateTimeOffset.UtcNow; User = "new.user@test.com" }
                ]
                
                for todo in todos do
                    do! insertTodo todo trx
                
                let! todoCount = trx.Connection.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM Todo")
                Assert.Equal(5, todoCount)
                
                let! existingUserTodos = getTodos "existing.user@test.com" trx.Connection
                Assert.Equal(2, Seq.length existingUserTodos)
                
                let! newUserTodos = getTodos "new.user@test.com" trx.Connection
                Assert.Single(newUserTodos) |> ignore
                
                // let's ensure the single lookup enforces the user
                let firstId = Seq.head todos |> _.Id
                let! todo = getTodo "existing.user@test.com" firstId trx.Connection
                Assert.NotNull(todo)
                // should not find the todo
                let! todo = getTodo "new.user@test.com" firstId trx.Connection
                Assert.Null(todo)
            }
        )