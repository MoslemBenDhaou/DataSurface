# Summary

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.7309)

* 13th Gen Intel Core i9-13900K, 1 CPU, 32 logical and 24 physical cores
* .NET SDK 10.0.100
  - [Host]     : .NET 9.0.11 (9.0.1125.51716), X64 RyuJIT AVX2
  - DefaultJob : .NET 9.0.11 (9.0.1125.51716), X64 RyuJIT AVX2

| Method                   | DataSize | Mean       | Error    | StdDev   | Median     | Ratio | RatioSD | Rank | Gen0    | Gen1    | Gen2    | Allocated | Alloc Ratio |
|------------------------- |--------- |-----------:|---------:|---------:|-----------:|------:|--------:|-----:|--------:|--------:|--------:|----------:|------------:|
| PaginationOnly           | 100      |   245.7 us |  3.63 us |  3.39 us |   245.6 us |  1.00 |    0.02 |    1 |       - |       - |       - |   8.56 KB |        1.00 |
| SingleEqFilter           | 100      |   390.4 us |  2.00 us |  1.77 us |   390.0 us |  1.59 |    0.02 |    2 |  0.4883 |       - |       - |  16.18 KB |        1.89 |
| IsNullFilter             | 100      |   549.8 us |  5.47 us |  5.12 us |   548.1 us |  2.24 |    0.04 |    3 |       - |       - |       - |  16.09 KB |        1.88 |
| ContainsFilter           | 100      |   572.1 us | 10.70 us | 10.99 us |   575.7 us |  2.33 |    0.05 |    3 |  0.9766 |       - |       - |  19.33 KB |        2.26 |
| SingleSort               | 100      |   609.9 us |  8.06 us |  7.54 us |   610.6 us |  2.48 |    0.04 |    4 |  0.9766 |       - |       - |  20.51 KB |        2.40 |
| InFilter                 | 100      |   623.9 us |  6.62 us |  6.19 us |   624.9 us |  2.54 |    0.04 |    4 |       - |       - |       - |  17.26 KB |        2.02 |
| FullTextSearch           | 100      |   697.4 us | 10.73 us | 10.04 us |   698.0 us |  2.84 |    0.05 |    5 |       - |       - |       - |  17.73 KB |        2.07 |
| MultiSort                | 100      |   756.8 us | 14.95 us | 13.99 us |   760.4 us |  3.08 |    0.07 |    6 |  0.9766 |       - |       - |  31.65 KB |        3.70 |
| MultipleFilters          | 100      |   772.7 us | 15.31 us | 33.29 us |   778.2 us |  3.15 |    0.14 |    6 |       - |       - |       - |  17.48 KB |        2.04 |
| FullTextSearchWithFilter | 100      |   893.7 us | 10.90 us | 10.20 us |   895.5 us |  3.64 |    0.06 |    7 |       - |       - |       - |  25.31 KB |        2.96 |
| FilterSortAndPaginate    | 100      |   983.6 us | 27.78 us | 81.91 us | 1,009.2 us |  4.00 |    0.34 |    8 |       - |       - |       - |  26.91 KB |        3.14 |
| ComplexQuery             | 100      | 1,674.5 us | 12.25 us | 11.46 us | 1,674.3 us |  6.82 |    0.10 |    9 |  1.9531 |       - |       - |  49.46 KB |        5.78 |
|                          |          |            |          |          |            |       |         |      |         |         |         |           |             |
| PaginationOnly           | 1000     |   401.7 us |  4.22 us |  3.74 us |   401.7 us |  1.00 |    0.01 |    1 |       - |       - |       - |   8.56 KB |        1.00 |
| IsNullFilter             | 1000     |   585.4 us |  3.48 us |  3.25 us |   585.1 us |  1.46 |    0.02 |    2 |       - |       - |       - |  16.04 KB |        1.87 |
| SingleEqFilter           | 1000     |   601.6 us |  1.92 us |  1.60 us |   601.2 us |  1.50 |    0.01 |    2 |       - |       - |       - |  16.24 KB |        1.90 |
| ContainsFilter           | 1000     |   610.0 us |  6.15 us |  5.75 us |   609.9 us |  1.52 |    0.02 |    2 |  0.9766 |       - |       - |  19.49 KB |        2.28 |
| InFilter                 | 1000     |   663.3 us |  2.89 us |  2.56 us |   662.8 us |  1.65 |    0.02 |    3 |       - |       - |       - |   17.1 KB |        2.00 |
| SingleSort               | 1000     |   667.3 us |  3.68 us |  3.44 us |   667.6 us |  1.66 |    0.02 |    3 |  1.9531 |  0.9766 |       - |  37.94 KB |        4.43 |
| FullTextSearch           | 1000     |   730.2 us |  3.10 us |  2.75 us |   730.5 us |  1.82 |    0.02 |    4 |       - |       - |       - |   17.7 KB |        2.07 |
| MultiSort                | 1000     |   821.4 us |  2.51 us |  2.35 us |   821.9 us |  2.04 |    0.02 |    5 |  2.9297 |  1.9531 |       - |  63.12 KB |        7.37 |
| MultipleFilters          | 1000     |   823.5 us |  3.61 us |  3.37 us |   822.8 us |  2.05 |    0.02 |    5 |       - |       - |       - |  17.92 KB |        2.09 |
| FullTextSearchWithFilter | 1000     |   921.9 us |  5.63 us |  5.26 us |   922.1 us |  2.29 |    0.02 |    6 |       - |       - |       - |  25.19 KB |        2.94 |
| FilterSortAndPaginate    | 1000     | 1,059.8 us |  4.03 us |  3.57 us | 1,058.8 us |  2.64 |    0.03 |    7 |       - |       - |       - |  32.22 KB |        3.76 |
| ComplexQuery             | 1000     | 1,699.9 us | 12.67 us | 11.85 us | 1,701.6 us |  4.23 |    0.05 |    8 |       - |       - |       - |   55.9 KB |        6.53 |
|                          |          |            |          |          |            |       |         |      |         |         |         |           |             |
| PaginationOnly           | 10000    |   296.5 us |  4.46 us |  4.17 us |   295.4 us |  1.00 |    0.02 |    1 |       - |       - |       - |   8.56 KB |        1.00 |
| IsNullFilter             | 10000    |   573.5 us |  5.91 us |  5.53 us |   574.9 us |  1.93 |    0.03 |    2 |       - |       - |       - |  16.09 KB |        1.88 |
| SingleEqFilter           | 10000    |   579.2 us | 11.54 us | 14.59 us |   584.2 us |  1.95 |    0.06 |    2 |       - |       - |       - |  16.13 KB |        1.88 |
| ContainsFilter           | 10000    |   618.4 us |  4.96 us |  4.64 us |   618.5 us |  2.09 |    0.03 |    3 |  0.9766 |       - |       - |  19.33 KB |        2.26 |
| InFilter                 | 10000    |   671.1 us |  3.19 us |  2.83 us |   670.4 us |  2.26 |    0.03 |    4 |       - |       - |       - |   17.1 KB |        2.00 |
| FullTextSearch           | 10000    |   734.3 us |  3.83 us |  3.58 us |   733.1 us |  2.48 |    0.04 |    5 |       - |       - |       - |  17.73 KB |        2.07 |
| MultipleFilters          | 10000    |   798.2 us |  3.63 us |  3.40 us |   797.9 us |  2.69 |    0.04 |    6 |       - |       - |       - |  17.69 KB |        2.07 |
| SingleSort               | 10000    |   834.9 us | 11.24 us |  9.97 us |   830.8 us |  2.82 |    0.05 |    7 | 10.7422 |  3.9063 |       - | 213.74 KB |       24.96 |
| FullTextSearchWithFilter | 10000    |   929.1 us |  4.09 us |  3.82 us |   929.6 us |  3.13 |    0.04 |    8 |       - |       - |       - |  25.19 KB |        2.94 |
| FilterSortAndPaginate    | 10000    | 1,220.6 us |  6.57 us |  6.14 us | 1,219.3 us |  4.12 |    0.06 |    9 |  3.9063 |  1.9531 |       - |  73.68 KB |        8.60 |
| MultiSort                | 10000    | 1,370.6 us | 13.06 us | 11.58 us | 1,370.0 us |  4.62 |    0.07 |   10 | 48.8281 | 48.8281 | 48.8281 | 379.98 KB |       44.37 |
| ComplexQuery             | 10000    | 2,040.3 us | 15.72 us | 13.93 us | 2,038.1 us |  6.88 |    0.10 |   11 |  3.9063 |       - |       - | 112.97 KB |       13.19 |

# Hints 
Outliers
  * QueryEngineBenchmarks.SingleEqFilter: Default           -> 1 outlier  was  removed (395.41 us)
  * QueryEngineBenchmarks.ContainsFilter: Default           -> 2 outliers were detected (547.74 us, 549.15 us)
  * QueryEngineBenchmarks.InFilter: Default                 -> 2 outliers were detected (604.25 us, 617.64 us)
  * QueryEngineBenchmarks.FullTextSearch: Default           -> 1 outlier  was  detected (673.57 us)
  * QueryEngineBenchmarks.MultiSort: Default                -> 1 outlier  was  detected (714.50 us)
  * QueryEngineBenchmarks.MultipleFilters: Default          -> 1 outlier  was  removed, 5 outliers were detected (546.51 us..751.44 us, 831.42 us)
  * QueryEngineBenchmarks.FilterSortAndPaginate: Default    -> 8 outliers were detected (680.69 us..950.43 us)
  * QueryEngineBenchmarks.PaginationOnly: Default           -> 1 outlier  was  removed (412.97 us)
  * QueryEngineBenchmarks.SingleEqFilter: Default           -> 2 outliers were removed (606.78 us, 608.78 us)
  * QueryEngineBenchmarks.InFilter: Default                 -> 1 outlier  was  removed (670.24 us)
  * QueryEngineBenchmarks.FullTextSearch: Default           -> 1 outlier  was  removed (737.00 us)
  * QueryEngineBenchmarks.MultiSort: Default                -> 2 outliers were detected (816.38 us, 817.01 us)
  * QueryEngineBenchmarks.FullTextSearchWithFilter: Default -> 1 outlier  was  detected (908.78 us)
  * QueryEngineBenchmarks.FilterSortAndPaginate: Default    -> 1 outlier  was  removed (1.07 ms)
  * QueryEngineBenchmarks.ComplexQuery: Default             -> 1 outlier  was  detected (1.67 ms)
  * QueryEngineBenchmarks.SingleEqFilter: Default           -> 3 outliers were detected (545.72 us..549.61 us)
  * QueryEngineBenchmarks.InFilter: Default                 -> 1 outlier  was  removed (678.65 us)
  * QueryEngineBenchmarks.SingleSort: Default               -> 1 outlier  was  removed (885.52 us)
  * QueryEngineBenchmarks.MultiSort: Default                -> 1 outlier  was  removed, 2 outliers were detected (1.35 ms, 1.39 ms)
  * QueryEngineBenchmarks.ComplexQuery: Default             -> 1 outlier  was  removed (2.09 ms)

# Legends
  * DataSize    : Value of the 'DataSize' parameter
  * Mean        : Arithmetic mean of all measurements
  * Error       : Half of 99.9% confidence interval
  * StdDev      : Standard deviation of all measurements
  * Median      : Value separating the higher half of all measurements (50th percentile)
  * Ratio       : Mean of the ratio distribution ([Current]/[Baseline])
  * RatioSD     : Standard deviation of the ratio distribution ([Current]/[Baseline])
  * Rank        : Relative position of current benchmark mean among all benchmarks (Arabic style)
  * Gen0        : GC Generation 0 collects per 1000 operations
  * Gen1        : GC Generation 1 collects per 1000 operations
  * Gen2        : GC Generation 2 collects per 1000 operations
  * Allocated   : Allocated memory per single operation (managed only, inclusive, 1KB = 1024B)
  * Alloc Ratio : Allocated memory ratio distribution ([Current]/[Baseline])
  * 1 us        : 1 Microsecond (0.000001 sec)

# Diagnostic Output - MemoryDiagnoser

* Run time: 00:12:41 (761.47 sec), executed benchmarks: 36
* Global total time: 00:13:36 (816.11 sec), executed benchmarks: 40