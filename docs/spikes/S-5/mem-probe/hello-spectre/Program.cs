using Spectre.Console;

AnsiConsole.MarkupLine("[green]hello[/]");
AnsiConsole.Write(new Table().AddColumn("a").AddRow("1"));
Thread.Sleep(4000);
