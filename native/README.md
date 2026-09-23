# native

Native libraries for the Unity app. Built on the developer machine and copied into `app/Assets/Plugins/x86_64/`; neither the source download nor the build folder is committed.

## Manifold (mesh booleans for the Body Studio)

[ADR-0005](../docs/adr/ADR-0005-body-designer-and-csg.md), [docs/08](../docs/08-body-designer-spec.md). Manifold v3.5.3, Apache-2.0, <https://github.com/elalish/manifold>.

```powershell
powershell -ExecutionPolicy Bypass -File native\manifold\build.ps1
```

Needs Visual Studio or the Build Tools with the C++ workload and "C++ CMake tools for Windows" (Build Tools 2026 18.x was used). The script:

1. sparse-clones the tagged release into `manifold/src/` (source folders only, about 2 MB);
2. configures [manifold/CMakeLists.txt](manifold/CMakeLists.txt): the Manifold core as a static library and the upstream C bindings (`bindings/c`, header `manifold/manifoldc.h`) compiled into **one** `manifoldc.dll` with the static MSVC runtime. No tests, no parallel backend, and the built-in `boolean2` 2D backend, so nothing else is downloaded;
3. builds Release (about 2 minutes) and copies `manifoldc.dll` (about 1.2 MB) and `manifold-LICENSE.txt` to `app/Assets/Plugins/x86_64/`.

Why one DLL with the static runtime: the Unity player itself does not depend on the Visual C++ runtime, so a plugin that did would fail to load on PCs without the Visual C++ Redistributable. One DLL also keeps every allocation and free in the same C runtime. `manifoldc.dll` imports only `KERNEL32.dll`.

C# calls the C API with `[DllImport("manifoldc")]` (see `app/Assets/Spike/Scripts/CsgSpike.cs`). Memory pattern of the C API: `manifold_alloc_*()` returns raw memory, a constructor such as `manifold_cube(mem, ...)` fills it, and `manifold_delete_*()` destroys and frees it. Results are lazy: booleans are evaluated when a result is first read (status, volume, mesh).

Licence: the Apache-2.0 text ships next to the DLL, and the credits must name Manifold.
