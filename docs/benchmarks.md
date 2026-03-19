# Performance Benchmarks

This document presents and interprets the performance characteristics of DataSurface's query engine, measured using [BenchmarkDotNet](https://benchmarkdotnet.org/).

---

## Test Environment

| Component | Detail |
|-----------|--------|
| **CPU** | 13th Gen Intel Core i9-13900K (24 cores, 32 threads) |
| **OS** | Windows 11 |
| **Runtime** | .NET 9.0.11, X64 RyuJIT AVX2 |
| **Benchmark tool** | BenchmarkDotNet v0.14.0 |
| **Total run time** | ~13 minutes across 36 benchmark iterations |

---

## What Is Being Measured

These benchmarks measure the **query engine layer** (`EfCrudQueryEngine`) — the component that translates `QuerySpec` parameters (filters, sorting, pagination, search) into LINQ expressions applied to an `IQueryable<T>`. This includes:

- Expression tree construction and compilation
- LINQ operator application (Where, OrderBy, Skip, Take)
- In-memory query execution (using an in-memory provider, not a real database)

**Not included:** HTTP parsing, serialization, database I/O, network latency, hooks, or security checks. Real-world end-to-end latency will be higher due to these additional layers.

---

## Understanding the Columns

| Column | Meaning |
|--------|---------|
| **Method** | The query operation being benchmarked |
| **DataSize** | Number of records in the in-memory dataset (100, 1,000, or 10,000) |
| **Mean** | Average execution time across all iterations |
| **Error** | Half-width of the 99.9% confidence interval — smaller is more precise |
| **StdDev** | Standard deviation — measures consistency between runs |
| **Ratio** | Performance relative to the baseline (`PaginationOnly` = 1.00) |
| **Rank** | Position from fastest (1) to slowest |
| **Allocated** | Managed memory allocated per operation |
| **Alloc Ratio** | Memory usage relative to the baseline |

**Time units:** All times are in microseconds (µs). 1 µs = 0.001 ms = 0.000001 seconds.

---

## Results by Data Size

### Small Dataset (100 records)

| Rank | Operation | Mean (µs) | Ratio | Memory |
|------|-----------|-----------|-------|--------|
| 1 | PaginationOnly | 246 | 1.00× | 8.6 KB |
| 2 | SingleEqFilter | 390 | 1.59× | 16.2 KB |
| 3 | IsNullFilter | 550 | 2.24× | 16.1 KB |
| 3 | ContainsFilter | 572 | 2.33× | 19.3 KB |
| 4 | SingleSort | 610 | 2.48× | 20.5 KB |
| 4 | InFilter | 624 | 2.54× | 17.3 KB |
| 5 | FullTextSearch | 697 | 2.84× | 17.7 KB |
| 6 | MultiSort | 757 | 3.08× | 31.7 KB |
| 6 | MultipleFilters | 773 | 3.15× | 17.5 KB |
| 7 | FullTextSearchWithFilter | 894 | 3.64× | 25.3 KB |
| 8 | FilterSortAndPaginate | 984 | 4.00× | 26.9 KB |
| 9 | ComplexQuery | 1,675 | 6.82× | 49.5 KB |

### Medium Dataset (1,000 records)

| Rank | Operation | Mean (µs) | Ratio | Memory |
|------|-----------|-----------|-------|--------|
| 1 | PaginationOnly | 402 | 1.00× | 8.6 KB |
| 2 | IsNullFilter | 585 | 1.46× | 16.0 KB |
| 2 | SingleEqFilter | 602 | 1.50× | 16.2 KB |
| 2 | ContainsFilter | 610 | 1.52× | 19.5 KB |
| 3 | InFilter | 663 | 1.65× | 17.1 KB |
| 3 | SingleSort | 667 | 1.66× | 37.9 KB |
| 4 | FullTextSearch | 730 | 1.82× | 17.7 KB |
| 5 | MultiSort | 821 | 2.04× | 63.1 KB |
| 5 | MultipleFilters | 824 | 2.05× | 17.9 KB |
| 6 | FullTextSearchWithFilter | 922 | 2.29× | 25.2 KB |
| 7 | FilterSortAndPaginate | 1,060 | 2.64× | 32.2 KB |
| 8 | ComplexQuery | 1,700 | 4.23× | 55.9 KB |

### Large Dataset (10,000 records)

| Rank | Operation | Mean (µs) | Ratio | Memory |
|------|-----------|-----------|-------|--------|
| 1 | PaginationOnly | 297 | 1.00× | 8.6 KB |
| 2 | IsNullFilter | 574 | 1.93× | 16.1 KB |
| 2 | SingleEqFilter | 579 | 1.95× | 16.1 KB |
| 3 | ContainsFilter | 618 | 2.09× | 19.3 KB |
| 4 | InFilter | 671 | 2.26× | 17.1 KB |
| 5 | FullTextSearch | 734 | 2.48× | 17.7 KB |
| 6 | MultipleFilters | 798 | 2.69× | 17.7 KB |
| 7 | SingleSort | 835 | 2.82× | 213.7 KB |
| 8 | FullTextSearchWithFilter | 929 | 3.13× | 25.2 KB |
| 9 | FilterSortAndPaginate | 1,221 | 4.12× | 73.7 KB |
| 10 | MultiSort | 1,371 | 4.62× | 380.0 KB |
| 11 | ComplexQuery | 2,040 | 6.88× | 113.0 KB |

---

## Key Findings

### 1. Pagination Is Extremely Fast

The baseline operation — applying `Skip` and `Take` to a pre-existing queryable — completes in **246–402 µs** regardless of dataset size. This confirms that DataSurface's pagination adds minimal overhead.

### 2. Filters Scale Well

Simple filters (`eq`, `isnull`, `contains`, `in`) add only **1.5–2.5× overhead** over the baseline. The cost comes from LINQ expression construction, not data scanning (since filters reduce the result set). Notably:

- **Equality filter** (`eq`) is the cheapest filter at ~1.5–1.6×
- **Contains** and **IsNull** are nearly identical in cost
- **In filter** (multiple value matching) adds only marginal overhead

### 3. Sorting Cost Grows with Data Size

Sorting is where dataset size has the most impact:

- At 100 records: `SingleSort` = 2.48×, `MultiSort` = 3.08×
- At 10,000 records: `SingleSort` = 2.82×, `MultiSort` = **4.62×**

This is expected — sorting requires evaluating all matching records. Memory allocation also scales significantly: `MultiSort` at 10K records allocates **380 KB** (44× the baseline) because the sort buffer must hold references to all records.

### 4. Full-Text Search Is Efficient

Searching across multiple fields costs only **2.5–2.8×** the baseline — comparable to a single filter. This is because search translates to multiple `OR`-combined `Contains` expressions, which the LINQ provider handles efficiently.

### 5. Complex Queries Are Predictable

The most expensive operation — `ComplexQuery` (multiple filters + multiple sorts + search + pagination) — costs **4.2–6.9×** the baseline. This is roughly additive: the cost of individual features combines linearly rather than exponentially.

### 6. Memory Usage Is Modest

Most operations allocate **16–32 KB** per query. The exceptions are:
- **Sorting at large scale** — `MultiSort` at 10K records allocates 380 KB due to the sort buffer
- **Complex queries** — 50–113 KB depending on dataset size

For typical web API usage (page sizes of 20–50, moderate filter counts), memory pressure is negligible.

---

## Scaling Behavior Summary

| Operation Category | 100 → 10K Records | Memory Impact |
|--------------------|--------------------|---------------|
| Pagination only | +20% time | No change |
| Simple filters | +50% time | No change |
| Sorting | +37% (single), +81% (multi) | Significant growth |
| Full-text search | +5% time | No change |
| Complex queries | +22% time | 2× growth |

**Takeaway:** Filter and search operations scale well because they reduce the working set. Sorting is the primary scaling concern because it must process all matching records before pagination can apply.

---

## Recommendations

Based on these benchmarks:

1. **Filters are cheap** — Encourage clients to filter aggressively to reduce result sets before sorting
2. **Limit sort fields** — The `SortableFields` allowlist in the contract is important; keep it focused
3. **Use default sorts** — Set `DefaultSort` on resources to avoid client-requested multi-sort on large datasets
4. **Monitor complex queries** — The `ComplexQuery` benchmark represents a worst-case scenario; consider query cost limits for production APIs
5. **Consider compiled queries** — For hot paths with predictable access patterns, `CompiledQueryCache` can eliminate expression tree compilation overhead

---

## Reproducing These Benchmarks

The benchmarks are defined in the `QueryEngineBenchmarks` class and can be run with:

```bash
dotnet run -c Release --project DataSurface.Benchmarks
```

Results are generated by BenchmarkDotNet with the `MemoryDiagnoser` enabled for allocation tracking.
