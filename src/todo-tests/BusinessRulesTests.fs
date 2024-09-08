namespace todo_tests

open System
open Xunit
open todo_api
open todo_api.Models
open todo_api.BusinessRules

type BusinessRulesTests () =
    
    [<Fact>]
    let ``Todos can only be completed if not completed`` () =
        let todo = { Id = Guid.NewGuid(); Description = "Test"; Completed = true; Created = DateTimeOffset.UtcNow; User = "test" }
        let result = completeTodo todo
        Assert.True(Result.isError result)
        match result with
        | Error (BadRequest _) -> Assert.True(true)
        | _ -> Assert.True(false)
        // now try with a non-completed todo
        let todo = { todo with Completed = false }
        let result = completeTodo todo
        Assert.True(Result.isOk result)
        
    [<Fact>]
    let ``Todos can be toggled`` () =
        let todo = { Id = Guid.NewGuid(); Description = "Test"; Completed = false; Created = DateTimeOffset.UtcNow; User = "test" }
        let toggled = toggleTodo todo
        Assert.True(toggled.Completed)
        let toggled = toggleTodo toggled
        Assert.False(toggled.Completed)
