#! /bin/bash
docker compose --profile dev up -d
dotnet run --project src/todo-api/todo-api.fsproj