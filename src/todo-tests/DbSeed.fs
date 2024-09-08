module todo_tests.DbSeed

open Dapper
open Npgsql

let dbSeed (db: NpgsqlConnection) =
    task {
        let! _ = db.ExecuteAsync("INSERT INTO Users (Email) VALUES ('existing.user@test.com') ON CONFLICT DO NOTHING")
        return ()
    }
    
let dbTruncate (db: NpgsqlConnection) =
    task {
        let! _ = db.ExecuteAsync("TRUNCATE TABLE Todo CASCADE")
        let! _ = db.ExecuteAsync("TRUNCATE TABLE Users CASCADE")
        return ()
    }
    

