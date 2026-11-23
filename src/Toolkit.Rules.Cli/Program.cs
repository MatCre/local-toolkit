using System.CommandLine;
using System.Text;
using Toolkit.Rules.Cli;

// System.CommandLine: each command declares its arguments and options, the library parses args
// into them, generates --help, and reports bad input. SetAction is the handler.

Console.OutputEncoding = Encoding.UTF8; // for "…" and other non-ASCII text on Windows consoles

var csvOption = new Option<FileInfo>("--csv")
{
    Description = "The bank's exported transactions CSV (Date, Description, Amount columns).",
    Required = true,
    Recursive = true,
};
var rulesOption = new Option<FileInfo?>("--rules")
{
    Description = $"Rules file. Default: {RulesCommands.DefaultRulesPath}",
    Recursive = true,
};

var categorize = new Command("categorize", "Apply rules to a transactions CSV and report how many rows matched. AI is never called.")
{
    csvOption, rulesOption,
};
categorize.SetAction((parse, ct) => RulesCommands.CategorizeAsync(parse.GetValue(csvOption)!, RulesPath(parse), ct));

var limitOption = new Option<int>("--limit") { Description = "Most distinct unmatched payees to ask about.", DefaultValueFactory = _ => 40 };
var proposedOption = new Option<FileInfo?>("--proposed")
{
    Description = $"The proposed-rules file: written by propose, read by apply. Default: {RulesCommands.DefaultProposedPath}",
    Recursive = true,
};
var propose = new Command(
    "propose",
    "Ask the local model once to propose rules for payees no rule currently matches, and write them to " +
    "a file for review. Runs on Ollama only, and the request is marked so it's never saved to the answer vault. " +
    "Doesn't change the rules file: run \"rules apply\" once you've looked them over.")
{
    csvOption, rulesOption, limitOption, proposedOption,
};
propose.SetAction((parse, ct) => RulesCommands.ProposeAsync(
    parse.GetValue(csvOption)!, RulesPath(parse), ProposedPath(parse), parse.GetValue(limitOption), ct));

var apply = new Command(
    "apply",
    "Add the rules from the last \"rules propose\" into the rules file. AI is never called: this applies " +
    "exactly what was written for review, not a fresh proposal.")
{
    rulesOption, proposedOption,
};
apply.SetAction((parse, ct) => RulesCommands.ApplyAsync(ProposedPath(parse), RulesPath(parse), ct));

var root = new RootCommand("Categorise bank transactions with rules a local model proposes once, then runs with AI switched off.")
{
    categorize, propose, apply,
};

return await root.Parse(args).InvokeAsync();

string RulesPath(ParseResult parse) => parse.GetValue(rulesOption)?.FullName ?? RulesCommands.DefaultRulesPath;

string ProposedPath(ParseResult parse) => parse.GetValue(proposedOption)?.FullName ?? RulesCommands.DefaultProposedPath;
