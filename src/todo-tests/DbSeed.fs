module todo_tests.DbSeed

open System
open Dapper
open Npgsql

let dbSeed (db: NpgsqlConnection) =
    task {
        let! _ = db.ExecuteAsync("INSERT INTO Users (Email) VALUES ('existing.user@test.com') ON CONFLICT DO NOTHING")
        let! _ = db.ExecuteAsync("INSERT INTO Todo (Id, Description, Completed, Created, LastModified, UserId) VALUES (@Id, 'Test', false, NOW(), NOW(), (SELECT UserId FROM Users WHERE Email = 'existing.user@test.com'))
                                 ON CONFLICT DO NOTHING", {| Id = Guid.NewGuid() |})
        return ()
    }
    
let dbTruncate (db: NpgsqlConnection) =
    task {
        let! _ = db.ExecuteAsync("TRUNCATE TABLE Todo CASCADE")
        let! _ = db.ExecuteAsync("TRUNCATE TABLE Users CASCADE")
        return ()
    }
    

