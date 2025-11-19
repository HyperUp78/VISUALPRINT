Drop your precompiled COM/Interop DLLs in this folder.

Notes:
- Only public classes and methods are discovered.
- Both instance and static methods are listed as nodes.
- Assemblies are added as Roslyn metadata references during Compile.
- On build, everything under this `com` folder is copied next to GREENPRINTS.exe.

Example:
- MyInterop.dll
- Vendor\Automation.dll
