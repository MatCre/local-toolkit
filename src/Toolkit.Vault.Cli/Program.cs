using System.CommandLine;
using System.Text;
using Toolkit.Vault;
using Toolkit.Vault.Sqlite;
using Toolkit.Vault.Cli;

// System.CommandLine: each command declares its arguments and options, the library parses
// args into them, generates --help, and reports bad input. SetAction is the handler.

Console.OutputEncoding = Encoding.UTF8; // for "…" and other non-ASCII text on Windows consoles

var dbOption = new Option<string?>("--db")
{
    Description = $"Vault file to use. Default: the TOOLKIT_VAULT_DB environment variable, else {SqliteVaultStore.DefaultDatabasePath}",
    Recursive = true, // accepted on every subcommand
};

// vault add
var titleOption = new Option<string?>("--title", "-t") { Description = "Title. Default: the first line of the text." };
var tagsOption = new Option<string?>("--tags") { Description = "Labels, comma- or space-separated, e.g. \"dotnet,sqlite\"." };
var sourceOption = new Option<string?>("--source", "-s") { Description = "Where it came from: claude, a URL, a book. Default: how the text was read." };
var fileOption = new Option<FileInfo?>("--file", "-f") { Description = "Read the text from this file." };
var clipboardOption = new Option<bool>("--clipboard", "-c") { Description = "Read the text from the clipboard." };
var allowSecretOption = new Option<bool>("--allow-secret") { Description = "Store the text even if it looks like it contains a key or password." };
var add = new Command("add", "Add an entry from --file, --clipboard or piped input. Text that looks like a secret is refused.")
{
    titleOption, tagsOption, sourceOption, fileOption, clipboardOption, allowSecretOption,
};
add.SetAction((parse, ct) => VaultCommands.AddAsync(
    Store(parse), parse.GetValue(titleOption), parse.GetValue(tagsOption), parse.GetValue(sourceOption), parse.GetValue(fileOption),
    parse.GetValue(clipboardOption), parse.GetValue(allowSecretOption), ct));

// vault search
var wordsArgument = new Argument<string[]>("words") { Description = "Words to find. Every word must match; endings and half-typed words are fine.", Arity = ArgumentArity.OneOrMore };
var searchLimitOption = new Option<int>("--limit", "-n") { Description = "Most results to show.", DefaultValueFactory = _ => 10 };
var search = new Command("search", "Find entries by words in their title, text or tags, best match first.") { wordsArgument, searchLimitOption };
search.SetAction((parse, ct) => VaultCommands.SearchAsync(
    Store(parse), string.Join(' ', parse.GetValue(wordsArgument)!), parse.GetValue(searchLimitOption), ct));

// vault show / remove
var idArgument = new Argument<long>("id") { Description = "Entry number, as shown by search or list." };
var show = new Command("show", "Print an entry in full.") { idArgument };
show.SetAction((parse, ct) => VaultCommands.ShowAsync(Store(parse), parse.GetValue(idArgument), ct));

var remove = new Command("remove", "Delete an entry permanently.") { idArgument };
remove.SetAction((parse, ct) => VaultCommands.RemoveAsync(Store(parse), parse.GetValue(idArgument), ct));

// vault list
var listLimitOption = new Option<int>("--limit", "-n") { Description = "How many to show.", DefaultValueFactory = _ => 20 };
var list = new Command("list", "Show the most recently added entries.") { listLimitOption };
list.SetAction((parse, ct) => VaultCommands.ListAsync(Store(parse), parse.GetValue(listLimitOption), ct));

var root = new RootCommand("Answer vault: keep useful answers and snippets, and find them again.")
{
    dbOption, add, search, show, list, remove,
};

return await root.Parse(args).InvokeAsync();

// The composition root: the only line in the CLI that names a database. Moving the vault to
// another provider means changing this line to create a different IVaultStore.
IVaultStore Store(ParseResult parse) => new SqliteVaultStore(
    parse.GetValue(dbOption)
    ?? Environment.GetEnvironmentVariable("TOOLKIT_VAULT_DB")
    ?? SqliteVaultStore.DefaultDatabasePath);
