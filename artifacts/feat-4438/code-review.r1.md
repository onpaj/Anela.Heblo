## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None. The helper body is byte-identical to both removed copies (R3), both call sites import the single definition (R1/R2), and the new unit test covers null, empty string and a formatted ISO value (R4).
