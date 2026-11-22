using System.Text;
using TkCli;

Console.OutputEncoding = Encoding.UTF8; // so non-ASCII text prints correctly on Windows consoles

return await Cli.Build().Parse(args).InvokeAsync();
