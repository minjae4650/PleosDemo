"""Parts for the districts of the Busan landmark map, split out of tools/build-gwangalli-blockout.py.

Every part appends triangles to the meshes of a `kit.Kit` and draws from its one random stream, so a district's output depends
on the order of its calls: keep that order fixed (the Gwangalli district reproduces the old generator's JSON byte for byte)."""
