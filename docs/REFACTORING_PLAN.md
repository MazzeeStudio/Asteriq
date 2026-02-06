# Asteriq Refactoring Plan
**Created**: 2026-02-06
**Status**: Not Started
**Estimated Total Effort**: 60-75 hours

---

## Overview

This refactoring plan addresses critical architectural issues discovered during code review:
- **God Object anti-pattern** (MainForm: 16,260 lines)
- **Tight coupling** (direct service instantiation)
- **Missing abstractions** (no DI, no logging framework)
- **Poor separation of concerns** (business logic in UI)
- **Monolithic CLI handler** (Program.cs: 2,182 lines)

---

## Task Dependencies

```
Phase 1: Foundation (CRITICAL - Do These First)
├─ Task #1: Set up tooling infrastructure [30 min]
│   └─ Creates: .editorconfig, Directory.Build.props, GlobalUsings.cs
│
├─ Task #3: Implement Dependency Injection [3-4 hours]
│   └─ Requires: Task #1
│   └─ Enables: All other tasks
│
├─ Task #4: Add structured logging [4-5 hours]
│   └─ Requires: Task #3 (DI container)
│
└─ Task #2: Fix DriverSetup files [2 hours]
    └─ Requires: Task #3, #4 (DI + logging)
    └─ BLOCKER: Must be done before committing

Phase 2: Service Layer Refactoring
├─ Task #5: Extract service interfaces [3-4 hours]
│   └─ Requires: Task #3
│
├─ Task #6: Split ProfileService [5-6 hours]
│   └─ Requires: Task #5
│
└─ Task #7: Extract business logic from MainForm [8-10 hours]
    └─ Requires: Task #5, #6

Phase 3: CLI Refactoring
└─ Task #8: Extract CLI command pattern [10-12 hours]
    └─ Requires: Task #3, #4, #5

Phase 4: UI Refactoring
└─ Task #9: Extract rendering logic [12-15 hours]
    └─ Requires: Task #7

Phase 5: Polish
├─ Task #10: Extract UI constants [4-5 hours]
│   └─ Requires: None (can be done anytime)
│
└─ Task #11: Update code review document [1 hour]
    └─ Requires: All previous tasks
```

---

## Immediate Critical Path

**START HERE** - These tasks must be completed in order:

### Week 1: Foundation
1. ✅ **Task #1**: Set up tooling infrastructure (30 min)
   - Create .editorconfig
   - Create Directory.Build.props
   - Create GlobalUsings.cs

2. ✅ **Task #3**: Implement Dependency Injection (3-4 hours)
   - Add Microsoft.Extensions.DependencyInjection
   - Create ServiceConfiguration.cs
   - Update Program.cs to build IServiceProvider
   - Update MainForm to accept injected services

3. ✅ **Task #4**: Add structured logging (4-5 hours)
   - Add Serilog packages
   - Configure logging in Program.cs
   - Update all services to use ILogger<T>
   - Replace Console.WriteLine/Debug.WriteLine

4. ✅ **Task #2**: Fix DriverSetup files (2 hours)
   - Refactor DriverSetupManager to use DI
   - Add logging to both files
   - Fix HttpClient usage
   - **COMMIT BLOCKER** - do this before committing staged changes

**Week 1 Total**: ~10-12 hours

### Week 2: Service Interfaces & Splitting
5. ✅ **Task #5**: Extract service interfaces (3-4 hours)
   - Create IInputService, IVJoyService, etc.
   - Update DI registration

6. ✅ **Task #6**: Split ProfileService (5-6 hours)
   - Extract ProfileRepository
   - Extract ApplicationSettingsService
   - Extract UIThemeService
   - Extract WindowStateManager

**Week 2 Total**: ~8-10 hours

### Week 3-4: Business Logic Extraction
7. ✅ **Task #7**: Extract business logic from MainForm (8-10 hours)
   - Create MappingManagementService
   - Create DeviceManagementService
   - Create SCBindingService
   - Move logic from MainForm to services

**Week 3-4 Total**: ~8-10 hours

### Week 5: CLI Refactoring
8. ✅ **Task #8**: Extract CLI command pattern (10-12 hours)
   - Create ICommand interface
   - Extract 17 commands to separate classes
   - Create CommandFactory
   - Reduce Program.cs to ~200 lines

**Week 5 Total**: ~10-12 hours

### Later: UI Polish (Can be deferred)
9. ⏸️ **Task #9**: Extract rendering logic (12-15 hours)
10. ⏸️ **Task #10**: Extract UI constants (4-5 hours)
11. ⏸️ **Task #11**: Update code review document (1 hour)

---

## Quick Start Guide

### Before Starting Any Work

```bash
# Ensure clean working directory
git status

# Create a refactoring branch
git checkout -b refactor/architecture-improvements

# OR work in smaller feature branches:
git checkout -b refactor/phase1-foundation
```

### Task #1: Tooling Infrastructure (START HERE)

**Time**: 30 minutes
**Risk**: Low
**Dependencies**: None

```bash
# 1. Create .editorconfig at solution root
# See: docs/REFACTORING_PLAN.md for template

# 2. Create Directory.Build.props at solution root

# 3. Create src/Asteriq/GlobalUsings.cs

# 4. Build and fix any warnings
dotnet build
dotnet test

# 5. Commit
git add .editorconfig Directory.Build.props src/Asteriq/GlobalUsings.cs
git commit -m "Add tooling infrastructure (editorconfig, build props, global usings)"
```

### Task #3: Dependency Injection (NEXT)

**Time**: 3-4 hours
**Risk**: Medium (touches Program.cs and MainForm)
**Dependencies**: Task #1

```bash
# 1. Add NuGet packages
cd src/Asteriq
dotnet add package Microsoft.Extensions.DependencyInjection
dotnet add package Microsoft.Extensions.Hosting

# 2. Create Services/ServiceConfiguration.cs
# See task details for implementation

# 3. Update Program.cs to build service provider

# 4. Update MainForm constructor to accept services

# 5. Test thoroughly - application should run exactly as before

# 6. Commit
git add .
git commit -m "Implement dependency injection container

- Add Microsoft.Extensions.DependencyInjection
- Create ServiceConfiguration for service registration
- Update Program.cs to build IServiceProvider
- Update MainForm to receive services via constructor
- Remove 'new Service()' instantiations from UI"
```

---

## Success Metrics

Track these metrics as you complete tasks:

| Metric | Before | Target | Current |
|--------|--------|--------|---------|
| MainForm total lines | 16,260 | <5,000 | - |
| Program.cs lines | 2,182 | <300 | - |
| Services with interfaces | 0 | 10+ | - |
| Console.WriteLine in services | 358 | 0 | - |
| Direct 'new Service()' in UI | 10+ | 0 | - |
| Average method length | ~80 lines | <30 lines | - |
| Test coverage | ~85% | >90% | - |

---

## Risk Mitigation

### High-Risk Changes
- **Task #3 (DI)**: Run full test suite after completion
- **Task #7 (Extract MainForm logic)**: Test all UI interactions manually
- **Task #8 (CLI commands)**: Test all 17 CLI commands

### Testing Strategy
```bash
# After each task, run:
dotnet test                    # Unit tests
dotnet run -- --help          # Verify CLI still works
dotnet run                    # Verify GUI launches

# Smoke test major features:
# - Device detection
# - Profile save/load
# - Mapping creation
# - SC binding export
```

### Rollback Plan
```bash
# If something breaks:
git checkout main
git branch -D refactor/feature-name

# Start over with smaller incremental changes
```

---

## Code Templates

### .editorconfig Template
```ini
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = crlf
charset = utf-8-bom
trim_trailing_whitespace = true
insert_final_newline = true

[*.cs]
# Naming rules
dotnet_naming_rule.private_fields_underscore.symbols = private_fields
dotnet_naming_rule.private_fields_underscore.style = underscore_prefix
dotnet_naming_rule.private_fields_underscore.severity = error

dotnet_naming_symbols.private_fields.applicable_kinds = field
dotnet_naming_symbols.private_fields.applicable_accessibilities = private

dotnet_naming_style.underscore_prefix.capitalization = camel_case
dotnet_naming_style.underscore_prefix.required_prefix = _

# Static fields (s_ prefix)
dotnet_naming_rule.static_fields_s_prefix.symbols = static_fields
dotnet_naming_rule.static_fields_s_prefix.style = s_prefix
dotnet_naming_rule.static_fields_s_prefix.severity = error

dotnet_naming_symbols.static_fields.applicable_kinds = field
dotnet_naming_symbols.static_fields.applicable_accessibilities = private, internal, private_protected
dotnet_naming_symbols.static_fields.required_modifiers = static

dotnet_naming_style.s_prefix.capitalization = camel_case
dotnet_naming_style.s_prefix.required_prefix = s_

# File-scoped namespaces
csharp_style_namespace_declarations = file_scoped:error

# Prefer pattern matching
csharp_style_prefer_pattern_matching = true:warning
csharp_style_pattern_matching_over_is_with_cast_check = true:warning

# Null checking
csharp_style_conditional_delegate_call = true:warning

# Code quality
dotnet_code_quality_unused_parameters = all:warning
```

### Directory.Build.props Template
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-all</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="8.*" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

### GlobalUsings.cs Template
```csharp
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
```

### Service Interface Template
```csharp
namespace Asteriq.Services.Abstractions;

/// <summary>
/// Service for managing [what it does]
/// </summary>
public interface IExampleService : IDisposable
{
    /// <summary>
    /// Does something important
    /// </summary>
    Task<Result> DoSomethingAsync(string parameter, CancellationToken ct = default);

    /// <summary>
    /// Fired when something happens
    /// </summary>
    event EventHandler<EventArgs>? SomethingHappened;
}
```

### Command Template
```csharp
namespace Asteriq.Commands;

public class ExampleCommand : ICommand
{
    private readonly ILogger<ExampleCommand> _logger;
    private readonly IExampleService _service;

    public string Name => "example";
    public string Description => "Does something useful";
    public string[] Aliases => ["--example", "-e"];

    public ExampleCommand(ILogger<ExampleCommand> logger, IExampleService service)
    {
        _logger = logger;
        _service = service;
    }

    public async Task<CommandResult> ExecuteAsync(string[] args)
    {
        _logger.LogInformation("Executing example command");

        try
        {
            // Command logic here
            await _service.DoSomethingAsync("param");

            return new CommandResult
            {
                Success = true,
                ExitCode = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Example command failed");
            return new CommandResult
            {
                Success = false,
                ExitCode = 1,
                Message = ex.Message
            };
        }
    }
}
```

---

## Questions & Answers

**Q: Can I work on Phase 5 tasks (constants extraction) while doing Phase 1?**
A: Yes! Task #10 (Extract UI constants) has no dependencies and can be done anytime for quick wins.

**Q: Should I commit after each task or batch them?**
A: Commit after each task completes successfully. Use conventional commit messages.

**Q: What if I find new issues during refactoring?**
A: Document them in a new section of this file, but don't derail the current plan. Finish the current phase first.

**Q: Can I skip the DriverSetup file fixes?**
A: NO. Task #2 is a commit blocker. Those files have critical issues and should not be committed as-is.

**Q: How do I test that refactoring didn't break anything?**
A: Run the full test suite (`dotnet test`) and manually test all major features. See Testing Strategy section.

---

## Progress Tracking

Update this section as tasks complete:

- [ ] Phase 1: Foundation (0/4 tasks complete)
- [ ] Phase 2: Service Layer (0/3 tasks complete)
- [ ] Phase 3: CLI Refactoring (0/1 tasks complete)
- [ ] Phase 4: UI Refactoring (0/1 tasks complete)
- [ ] Phase 5: Polish (0/2 tasks complete)

**Overall Progress**: 0/11 tasks complete (0%)

---

## Notes

Add implementation notes, blockers, or discoveries here as you work:

-

---

## References

- [C# Development Guidelines](../.claude/csharp_dev_guidelines.md)
- [Code Review Document](../.claude/asteriq_code_review.md)
- [SOLID Principles](https://en.wikipedia.org/wiki/SOLID)
- [Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection)
- [Serilog Documentation](https://serilog.net/)
