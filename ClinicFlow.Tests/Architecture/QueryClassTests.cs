using System.Reflection;
using System.Reflection.Emit;
using Microsoft.EntityFrameworkCore;
using Persistence.AppData;
using Persistence.Queries;

namespace ClinicFlow.Tests.Architecture
{
    // Query classes (CQRS-lite, docs/v2/06 §2) read and never write. This reads the IL of every method in
    // Persistence.Queries, including the async state machines and lambdas the compiler generates, and fails on
    // any EF Core call that saves, tracks a change or runs a bulk update or delete
    public class QueryClassTests
    {
        private static readonly string[] WriteMethodPrefixes =
            ["SaveChanges", "Add", "Update", "Remove", "Attach", "Entry", "ExecuteUpdate", "ExecuteDelete", "ExecuteSql"];

        private static readonly Dictionary<short, OpCode> ByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value);

        [Fact]
        public void QueryClasses_NeverWrite()
        {
            var queryTypes = typeof(AppointmentQueries).Assembly.GetTypes()
                .Where(t => t.Namespace == typeof(AppointmentQueries).Namespace)
                .ToList();

            Assert.NotEmpty(queryTypes);
            var writes = Writes(queryTypes);
            Assert.True(writes.Count == 0,
                $"Query classes must not write (docs/v2/06 §2):{Environment.NewLine}{string.Join(Environment.NewLine, writes)}");
        }

        [Fact]
        public void Writes_FindsAPlantedSave()
        {
            // Guards the IL reader itself, against a class in this assembly that saves inside an async method
            Assert.Contains(Writes([typeof(WritingQueries)]), w => w.Contains("SaveChangesAsync"));
            Assert.Contains(Writes([typeof(WritingQueries)]), w => w.Contains("ExecuteDeleteAsync"));
        }

        private sealed class WritingQueries(ClinicDbContext context)
        {
            public async Task RenameAsync(CancellationToken ct)
            {
                await context.SaveChangesAsync(ct);
                await context.Appointments.Where(a => a.Id == 0).ExecuteDeleteAsync(ct);
            }
        }

        // "Type.Method → EF method" for every write call in the types and the types the compiler nested in them
        private static List<string> Writes(IEnumerable<Type> types)
        {
            var writes = new List<string>();
            foreach (var type in types.SelectMany(WithNested))
            {
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
                {
                    foreach (var called in CalledMethods(method))
                    {
                        if (IsEfWrite(called))
                            writes.Add($"{type.FullName}.{method.Name} → {called.DeclaringType?.Name}.{called.Name}");
                    }
                }
            }
            return writes;
        }

        private static IEnumerable<Type> WithNested(Type type)
            => new[] { type }.Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(WithNested));

        private static bool IsEfWrite(MethodBase method)
        {
            var declaring = method.DeclaringType;
            var fromEf = declaring?.Assembly.GetName().Name?.StartsWith("Microsoft.EntityFrameworkCore") == true
                || typeof(DbContext).IsAssignableFrom(declaring);
            return fromEf && WriteMethodPrefixes.Any(method.Name.StartsWith);
        }

        // The methods a method body calls, read from its IL (call, callvirt, newobj, ldftn)
        private static IEnumerable<MethodBase> CalledMethods(MethodBase method)
        {
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il is null) yield break;

            var typeArgs = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null;
            var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

            for (var i = 0; i < il.Length;)
            {
                var code = il[i] == 0xFE ? ByValue[unchecked((short)(0xFE00 | il[i + 1]))] : ByValue[il[i]];
                i += code.Size;

                if (code.OperandType == OperandType.InlineMethod)
                {
                    var token = BitConverter.ToInt32(il, i);
                    MethodBase? called = null;
                    try { called = method.Module.ResolveMethod(token, typeArgs, methodArgs); } catch (ArgumentException) { }
                    if (called is not null) yield return called;
                }

                i += code.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                    _ => 4,
                };
            }
        }
    }
}
