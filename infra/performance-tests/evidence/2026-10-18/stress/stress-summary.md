# Stress test: FAIL

Execution was incomplete or failed without an established capacity limit. No capacity limit was found; passing tested loads does not prove maximum capacity.

| Multiplier | Virtual users | Samples | Errors | Verdict |
| ---: | ---: | ---: | ---: | --- |
| 1 | 20 | 906 | 0 | PASS |
| 2 | 40 | 1812 | 0 | PASS |
| 4 | 80 | 3624 | 0 | PASS |
| 8 | 160 | 7239 | 1 | FAIL |

Failures:
- Stress multiplier 8 failed; inspect its summary
