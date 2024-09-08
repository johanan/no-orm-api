#! /bin/bash
docker compose --profile test up -d
dotnet test src/todo-tests/todo-tests.fsproj