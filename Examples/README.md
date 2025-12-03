## Example Programs

This directory contains two minimal examples showing how to use **VDRIVE** programmatically to:

1. Search for a known floppy image by name (`DATA4.D64`)
2. Mount it by name (number works too, but names are more reliable)
3. LOAD a known file (portal) from that mounted disk into RAM, optionally using the secondary address to control the load location

### `testapimin-bas.bas`
A short, plain‑text BASIC program demonstrating the search → mount → load sequence with minimal code.

### `testapimin-ml.prg`
A Machine Language version of the same workflow, written for the **ACME assembler**, useful for integrating VDRIVE calls into assembly projects.

---
