# Revit 2024 Development Guide

## Target Framework

- **.NET Framework 4.8** (not .NET 8)
- **Platform**: x64
- **Language**: C# 9.0 (LangVersion)

## Revit API References

```xml
<Reference Include="RevitAPI">
  <HintPath>$(RevitApiDir)\RevitAPI.dll</HintPath>
  <Private>false</Private>
</Reference>
<Reference Include="RevitAPIUI">
  <HintPath>$(RevitApiDir)\RevitAPIUI.dll</HintPath>
  <Private>false</Private>
</Reference>
```

`Private=false` ensures RevitAPI/RevitAPIUI are never copied to the output — they are loaded by Revit itself.

## API Discovery

The build system probes for `RevitAPI.dll` in order:
1. `-p:RevitApiDir=...` build argument
2. `%REVIT_2024_PATH%` environment variable
3. `C:\Program Files\Autodesk\Revit 2024`
4. `D:\REVIT\2024\Revit 2024` (alternate root)

## Key API Patterns

### ExternalEvent (Main-thread Bridge)
```csharp
// Background thread:
dispatcher.Enqueue(new MainThreadWorkItem((app, doc) => {
    // This runs on the Revit main thread
    using (var t = new Transaction(doc, "MyOp")) {
        t.Start();
        // Revit API calls here
        t.Commit();
    }
}, "My Operation"));

// MainThreadDispatcher raises ExternalEvent, Revit calls Execute():
public void Execute(UIApplication app) {
    // Process queue
}
```

### TransactionGroup (Atomic Execution)
```csharp
var tg = new TransactionGroup(doc, "Plan");
tg.Start();

// Operations run in individual Transactions inside the group
// ...

if (anyFailed) {
    tg.RollBack();  // All changes undone
} else {
    tg.Assimilate();  // Changes committed
}
```

### Unit Conversion
Revit's internal unit is decimal feet. Use `UnitNames.ToFeet()` for conversions:
```csharp
double mmValue = 5000;
double feetValue = UnitNames.ToFeet(mmValue, ExternalUnit.Mm);
```

Never hardcode `/ 304.8`.

### FilteredElementCollector
```csharp
var walls = new FilteredElementCollector(document)
    .OfCategory(BuiltInCategory.OST_Walls)
    .WhereElementIsNotElementType();
```

### Family Instance Creation
```csharp
if (!symbol.IsActive) {
    symbol.Activate();
    doc.Regenerate();
}
var instance = doc.Create.NewFamilyInstance(point, symbol, host, StructuralType.NonStructural);
```

## Revit 2024 API Notes

- `Document.Export()` for PDF uses `PDFExportOptions` (introduced in 2023, stable in 2024)
- `Document.Export()` for DWG uses `DWGExportOptions`
- `ViewPlan.Create()` takes `ViewFamilyType.Id` and `Level.Id`
- `Viewport.Create()` takes `sheetId`, `viewId`, and `XYZ` location
- `ElementTransformUtils.MoveElement()` and `RotateElement()` are static helpers

## What NOT to Do

- ❌ Call Revit API from a background thread
- ❌ Use `Task.Run()` for Revit API calls
- ❌ Use Revit 2025+ APIs (e.g., `OverrideGraphicSettings` changes)
- ❌ Hardcode unit conversions (`/ 304.8`)
- ❌ Use `FirstElement()` as a default type
- ❌ Leave `NotImplementedException` in supported operations
