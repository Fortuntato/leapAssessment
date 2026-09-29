# Exercise 3 — Product Search with LINQ (Skeleton)

Implement `IProductService.SearchAsync` to filter, sort, page, and project to DTO efficiently.

## Requirements
- Case-insensitive search on Name/Sku
- Optional Category filter
- Sort by CreatedUtc | Price | Name (asc/desc)
- Paging with cap of 100
- Return DTOs (no EF entities)

## Run
```bash
dotnet test
```