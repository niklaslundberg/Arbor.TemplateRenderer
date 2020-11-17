using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Dynamic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Scriban;
using Serilog;
using Zio;
using Zio.FileSystems;
using Arbor.FS;
using Humanizer;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Scriban.Parsing;
using Scriban.Syntax;
using Serilog.Core;

namespace Arbor.TemplateRenderer.Tool
{
    public class App : IAsyncDisposable
    {
        private readonly InputArgs _args;
        private readonly ILogger _logger;
        private readonly IFileSystem _fileSystem;

        public App(IFileSystem fileSystem, InputArgs args, ILogger logger)
        {
            _fileSystem = fileSystem;
            _args = args;
            _logger = logger;
        }

        public ValueTask DisposeAsync()
        {
            _fileSystem.Dispose();

            return default;
        }

        public Task<int> Execute()
        {
            return ExecuteInternal();
        }

        private async Task<int> ExecuteInternal()
        {
            var jsonFile = _args.ModelFile.NormalizePath();

            var json = _fileSystem.ReadAllText(jsonFile, Encoding.UTF8);
            var expConverter = new ExpandoObjectConverter();
             dynamic obj = JsonConvert.DeserializeObject<ExpandoObject>(json, expConverter);

            var templateFile = _args.TemplateFile.NormalizePath();

            var templateString = _fileSystem.ReadAllText(templateFile, Encoding.UTF8);

            bool GetMember(TemplateContext context, SourceSpan span, object target, string member, out object value)
            {
                if (target is not IDictionary<string, object> dict)
                {
                    value = default;
                    return false;
                }

                if (context.CurrentNode is ScriptMemberExpression memberExpression)
                {
                    string propertyName = memberExpression.Member.Name.Pascalize();

                    if (!dict.TryGetValue(propertyName, out var s))
                    {
                        value = default;
                        return false;
                    }

                    value = s;
                    return true;
                }



                if (!dict.TryGetValue(member, out var found))
                {
                    value = default;
                    return false;
                }

                value = found;
                return true;
            }

            bool TryGetVariable(TemplateContext context, SourceSpan span, ScriptVariable variable, out object value)
            {
                if (obj is not IDictionary<string, object> dict)
                {
                    value = default;
                    return false;
                }

                var normalizedKey = variable.Name.Pascalize();

                if (!dict.TryGetValue(normalizedKey, out var found))
                {
                    value = default;
                    return false;
                }

                value = found;
                return true;

            }

            //obj = new {Mappings = new List<object>() {new
            //{
            //    Key = "TestKey",
            //    Value = "TestValue",
            //    IsAsyncHandle = true
            //}}};

            var template = Template.Parse(templateString);

            //var result =  await template.RenderAsync((object)obj);
            var templateContext = new TemplateContext()
            {
                TryGetMember = GetMember,
                TryGetVariable = TryGetVariable
            };
            var result = await template.RenderAsync(templateContext);

            _logger.Information("{Message}", result);

            return 0;
        }


        public static App Create(ImmutableArray<string> args, IFileSystem fileSystem = default, ILogger logger = null)
        {
            if (args.Length != 2) throw new InvalidOperationException("Expected exactly 2 arguments bot got " + args.Length);
            InputArgs inputArgs = new InputArgs(args[0], args[1]);

            return new App(fileSystem ?? new PhysicalFileSystem(), inputArgs, logger ?? Logger.None);
        }

        public static async Task<int> CreateAndExecute(string[] args)
        {
            using var logger = new LoggerConfiguration().WriteTo.Console()
                .CreateLogger();
            try
            {
                await using var app = Create(args.ToImmutableArray(), logger:logger);

                return await app.Execute();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to run app");
                return 1;
            }
        }
    }
}
