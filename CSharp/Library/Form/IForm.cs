﻿using Microsoft.Bot.Builder.Form.Advanced;
using System;
using System.Collections.Generic;

namespace Microsoft.Bot.Builder.Form
{
    public delegate bool ConditionalDelegate<T>(T state);
    public delegate void CompletionDelegate<T>(ISession session, T state);

    public interface IForm<T> : IDialog
        where T : class, new()
    {
        FormConfiguration Configuration();

        IForm<T> Message(string message, ConditionalDelegate<T> condition = null);
        IForm<T> Message(Prompt prompt, ConditionalDelegate<T> condition = null);

        IForm<T> Field(string name, ConditionalDelegate<T> condition = null);

        IForm<T> Field(string name, string prompt, ConditionalDelegate<T> condition = null);

        IForm<T> Field(string name, Prompt prompt, ConditionalDelegate<T> condition = null);

        IForm<T> Field(IField<T> field);

        IForm<T> AddRemainingFields(IEnumerable<string> exclude = null);

        IForm<T> Confirm(string prompt = null, ConditionalDelegate<T> condition = null, IEnumerable<string> dependencies = null);

        IForm<T> Confirm(Prompt prompt, ConditionalDelegate<T> condition = null, IEnumerable<string> dependencies = null);

        IForm<T> Confirm(IFieldPrompt<T> field);

        IForm<T> OnCompletion(CompletionDelegate<T> callback);

        // TODO: IForm<T> Step(IStep<T);

        IFields<T> Fields();

        // TODO: Maybe add a Verify() that would check strings for being present run on first usage?

        // TODO: ILocalizer Localizer();

        // void SetLocalizer(ILocalizer localizer);
    }

    public class FormResult<T> : DialogResult
    {
        public readonly T State;
        public FormResult(string id, T state)
        {
            ChildId = id;
            State = state;
            Completed = true;
        }

        public FormResult(string id)
        {
            ChildId = id;
            Completed = false;
            Exception = null;
        }

        public FormResult(string id, Exception exception)
        {
            ChildId = id;
            Completed = false;
            Exception = exception;
        }
    }

    public enum FormCommand { Backup, Help, Quit, Reset, Status };

    public class CommandDescription
    {
        public string Description;
        public string[] Terms;
        public string Help;

        public CommandDescription(string description, string[] terms, string help)
        {
            Description = description;
            Terms = terms;
            Help = help;
        }
    }

    public class FormConfiguration
    {
        public Prompt DefaultPrompt = new Prompt("")
        {
            AllowDefault = BoolDefault.Yes,
            AllowNumbers = BoolDefault.Yes,
            Case = PromptNormalization.Auto,
            Feedback = FeedbackOptions.Auto,
            Format = "{0}. {1}",
            LastSeparator = " and ",
            Separator = ", ",
            Style = PromptStyle.Auto
        };
        public string NavigationFormat = "{&} ({})";
        public string StatusFormat = "{&}: {}";
        public string Unspecified = "Unspecified";
        public string[] NoPreference = new string[] { "No Preference", "none", "I don'?t care" };
        public string[] CurrentChoice = new string[] { "Current Choice", "current" };

        public List<Template> Templates = new List<Template>
        {
            // {0} is term being clarified
            new Template(TemplateUsage.Clarify, "By \"{0}\" {&} did you mean {||}"),
            new Template(TemplateUsage.CurrentChoice, "Current Choice ({})"),

            // {0} is the not understood term
            new Template(TemplateUsage.Feedback, "For {&} I understood {}. {?\"{0}\" is not an option.}"),

            // For help {0} is recognizer help and {1} is command help.
            new Template(TemplateUsage.Help, "You are filling in the {&} field.  Possible responses:\n{0}\n{1}"),
            new Template(TemplateUsage.HelpClarify, "You are clarifying a {&} value.  Possible responses:\n{0}\n{1}"),

            // {0} is list of field names.
            new Template(TemplateUsage.HelpNavigation, "You can switch to a field by using its name: ({0})."),

            // {0} is min, {1} is max and {2} are the terms people can type
            new Template(TemplateUsage.HelpOneNumber, "You can enter a number {0}-{1} or words from the descriptions. ({2})"),
            new Template(TemplateUsage.HelpManyNumber, "You can enter one or more numbers {0}-{1} or words from the descriptions. ({2})"),

            // {0} are the words people can type
            new Template(TemplateUsage.HelpOneWord, "You can enter in any words from the descriptions. ({0})"),
            new Template(TemplateUsage.HelpManyWord, "You can enter in one or more selections from the descriptions. ({0})"),
            new Template(TemplateUsage.HelpNavigation, "You can switch to another field by entering its name. ({0})."),

            new Template(TemplateUsage.NextStep, "What do you want to change? {||}"),
            new Template(TemplateUsage.NoPreference, "No Preference"),

            // {0} is the term that is not understood
            new Template(TemplateUsage.NotUnderstood, @"""{0}"" is not a {&} option."),

            new Template(TemplateUsage.SelectOne, "Please select a {&} {||}"),
            new Template(TemplateUsage.SelectMany, "Please select one or more {&} {||}"),

            new Template(TemplateUsage.String, "Please enter {&} {||}")
        };

        public Dictionary<FormCommand, CommandDescription> Commands = new Dictionary<FormCommand, CommandDescription>()
        {
            {FormCommand.Backup, new CommandDescription("Backup", new string[] {"backup", "go back", "back" }, 
                "Back: Go back to the previous question.") },
            {FormCommand.Help, new CommandDescription("Help", new string[] { "help", "choices", @"\?" }, 
                "Help: Show the kinds of responses you can enter.") },
            {FormCommand.Quit, new CommandDescription("Quit", new string[] { "quit", "stop", "finish", "goodbye", "good bye"}, 
                "Quit: Quit the form without completing it.") },
            {FormCommand.Reset, new CommandDescription("Start over", new string[] { "start over", "reset", "clear" }, 
                "Reset: Start over filling in the form.  (With defaults of your previous entries.)" ) },
            {FormCommand.Status, new CommandDescription("status", new string[] {"status", "progress", "so far" }, 
                "Status: Show your progress in filling in the form so far.") }
        };
    };
}

