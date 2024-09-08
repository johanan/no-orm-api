namespace todo_tests

open System
open System.Net
open System.Net.Http.Json
open Xunit
open todo_api.Models
open todo_tests.Utils

[<Collection("Sequential")>]
type ApiTests () =
    let factory = new Factory()
    do Factory.SeedDb factory

    interface IDisposable with
        member this.Dispose() =
            factory.Truncate()
            factory.Dispose()
    
    [<Fact>]
    member _.``Get All todos for user`` () =
        task {
            use client = factory.CreateClient()
            let! response = client.GetAsync("/todos/existing.user@test.com") |> AssertOk |> Deserialize<Todo[]>
            Assert.Single(response) |> ignore
        }
    
    [<Fact>]
    member _.``Create todo`` () =
        task {
            use client = factory.CreateClient()
            let todo = { Description = "" }
            let! _ = client.PostAsJsonAsync("/todos/existing.user@test.com", todo) |> AssertStatusCode HttpStatusCode.BadRequest
            let todo = { Description = null }
            let! _ = client.PostAsJsonAsync("/todos/existing.user@test.com", todo) |> AssertStatusCode HttpStatusCode.BadRequest
            let todo = { Description = "Test" }
            let! saved = client.PostAsJsonAsync("/todos/existing.user@test.com", todo) |> AssertOk |> Deserialize<Todo>
            Assert.Equal(todo.Description, saved.Description)
            Assert.Equal("existing.user@test.com", saved.User)
            return ()
        }
    
    [<Fact>]
    member _.``Complete todo`` () =
        task {
            use client = factory.CreateClient()
            let! todos = client.GetAsync("/todos/existing.user@test.com") |> AssertOk |> Deserialize<Todo[]>
            let id = todos.[0].Id.ToString()
            let! _ = client.PostAsync($"/todos/existing.user@test.com/{id}/complete", null) |> AssertOk
            let! _ = client.PostAsync($"/todos/existing.user@test.com/{id}/complete", null) |> AssertStatusCode HttpStatusCode.BadRequest
            return ()
        }
        
    [<Fact>]
    member _.``Toggle todo`` () =
        task {
            use client = factory.CreateClient()
            let! todos = client.GetAsync("/todos/existing.user@test.com") |> AssertOk |> Deserialize<Todo[]>
            let id = todos.[0].Id.ToString()
            let! completed = client.PostAsync($"/todos/existing.user@test.com/{id}/toggle", null) |> AssertOk |> Deserialize<Todo>
            Assert.True(completed.Completed)
            let! uncompleted = client.PostAsync($"/todos/existing.user@test.com/{id}/toggle", null) |> AssertOk |> Deserialize<Todo>
            Assert.False(uncompleted.Completed)
            return ()
        }
    
    [<Theory>]
    [<InlineData("/ /13F2EB21-93F0-4E2F-852F-C5ED8E49D3C8/complete", HttpStatusCode.Unauthorized)>]
    [<InlineData("/existing.user@test.com/13F2EB21-93F0-4E2F-852F-C5ED8E49D3C8/complete", HttpStatusCode.NotFound)>]
    [<InlineData("/ /13F2EB21-93F0-4E2F-852F-C5ED8E49D3C8/toggle", HttpStatusCode.Unauthorized)>]
    [<InlineData("/existing.user@test.com/13F2EB21-93F0-4E2F-852F-C5ED8E49D3C8/toggle", HttpStatusCode.NotFound)>]
    member _.``Updating todos with bad data`` (url, expected) =
        task {
            use client = factory.CreateClient()
            let! _ = client.PostAsync($"/todos{url}", null) |> AssertStatusCode expected
            return ()
        }