﻿// 
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license.
// 
// Microsoft Bot Framework: http://botframework.com
// 
// Bot Builder SDK Github:
// https://github.com/Microsoft/BotBuilder
// 
// Copyright (c) Microsoft Corporation
// All rights reserved.
// 
// MIT License:
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Bot.Builder.Internals;
using Microsoft.Bot.Builder.Form.Advanced;
using Microsoft.Bot.Builder.Luis;

namespace Microsoft.Bot.Builder.Form
{
    public static class FormDialog
    {
        public static IFormDialog<T> FromType<T>() where T : class, new()
        {
            return new FormDialog<T>(new T());
        }

        public static IFormDialog<T> FromForm<T>(MakeForm<T> makeForm) where T : class, new()
        {
            return new FormDialog<T>(new T(), makeForm);
        }


        #region IForm<T> statics
#if DEBUG
        internal static bool DebugRecognizers = false;
#endif
        #endregion
    }

    [Flags]
    public enum FormOptions { None, PromptInStart };

    public delegate IForm<T> MakeForm<T>();

    /// <summary>
    /// Form dialog manager for to fill in your state.
    /// </summary>
    /// <typeparam name="T">The type to fill in.</typeparam>
    /// <remarks>
    /// This is the root class for creating a form.  To use it, you:
    /// * Create an instance of this class parameterized with the class you want to fill in.
    /// * Optionally use the fluent API to specify the order of fields, messages and confirmations.
    /// * Register with the global dialog collection.
    /// * Start the form dialog.
    /// </remarks>
    [Serializable]
    public sealed class FormDialog<T> : IFormDialog<T>, ISerializable
        where T : class
    {
        // constructor arguments
        private readonly T _state;
        private readonly MakeForm<T> _makeForm;
        private readonly IEnumerable<EntityRecommendation> _entities;
        private readonly FormOptions _options;

        // instantiated in constructor, saved when serialized
        private readonly FormState _formState;

        // instantiated in constructor, re-instantiated when deserialized
        private readonly IForm<T> _form;
        private readonly IRecognize<T> _commands;

        private static IForm<T> MakeDefaultForm()
        {
            return new FormBuilder<T>().AddRemainingFields().Build();
        }

        public FormDialog(T state, MakeForm<T> makeForm = null, FormOptions options = FormOptions.None, IEnumerable<EntityRecommendation> entities = null, CultureInfo cultureInfo = null)
        {
            makeForm = makeForm ?? MakeDefaultForm;
            entities = entities ?? Enumerable.Empty<EntityRecommendation>();
            cultureInfo = cultureInfo ?? CultureInfo.InvariantCulture;

            // constructor arguments
            Fibers.Field.SetNotNull(out this._state, nameof(state), state);
            Fibers.Field.SetNotNull(out this._makeForm, nameof(makeForm), makeForm);
            Fibers.Field.SetNotNull(out this._entities, nameof(entities), entities);
            this._options = options;

            // make our form
            var form = _makeForm();

            // instantiated in constructor, saved when serialized
            this._formState = new FormState(form.Steps.Count, cultureInfo);

            // instantiated in constructor, re-instantiated when deserialized
            this._form = form;
            this._commands = this._form.BuildCommandRecognizer();
        }

        private FormDialog(SerializationInfo info, StreamingContext context)
        {
            // constructor arguments
            Fibers.Field.SetNotNullFrom(out this._state, nameof(this._state), info);
            Fibers.Field.SetNotNullFrom(out this._makeForm, nameof(this._makeForm), info);
            Fibers.Field.SetNotNullFrom(out this._entities, nameof(this._entities), info);
            this._options = info.GetValue<FormOptions>(nameof(this._options));

            // instantiated in constructor, saved when serialized
            Fibers.Field.SetNotNullFrom(out this._formState, nameof(this._formState), info);

            // instantiated in constructor, re-instantiated when deserialized
            this._form = _makeForm();
            this._commands = this._form.BuildCommandRecognizer();
        }

        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            // constructor arguments
            info.AddValue(nameof(this._state), this._state);
            info.AddValue(nameof(this._makeForm), this._makeForm);
            info.AddValue(nameof(this._entities), this._entities);
            info.AddValue(nameof(this._options), this._options);

            // instantiated in constructor, saved when serialized
            info.AddValue(nameof(this._formState), this._formState);
        }

        #region IForm<T> implementation

        IForm<T> IFormDialog<T>.Form { get { return this._form; } }

        #endregion

        #region IDialog implementation

        async Task IDialog.StartAsync(IDialogContext context)
        {
            var entityGroups = (from entity in this._entities group entity by entity.Type);
            foreach (var entityGroup in entityGroups)
            {
                var step = _form.Step(entityGroup.Key);
                if (step != null)
                {
                    _formState.Step = _form.StepIndex(step);
                    _formState.StepState = null;
                    var builder = new StringBuilder();
                    foreach (var entity in entityGroup)
                    {
                        builder.Append(entity.Entity);
                        builder.Append(' ');
                    }
                    var input = builder.ToString();
                    string feedback;
                    string prompt = step.Start(context, _state, _formState);
                    var matches = MatchAnalyzer.Coalesce(step.Match(context, _state, _formState, input, out prompt), input);
                    if (MatchAnalyzer.IsFullMatch(input, matches, 0.5))
                    {
                        // TODO: In the case of clarification I could
                        // 1) Go through them while supporting only quit or back and reset
                        // 2) Drop them
                        // 3) Just pick one (found in form.StepState, but that is opaque here)
                        var result = await step.ProcessAsync(context, _state, _formState, input, matches);
                        feedback = result.Feedback;
                        prompt = result.Prompt;
                    }
                    else
                    {
                        _formState.SetPhase(StepPhase.Ready);
                    }
                }
            }
            _formState.Step = 0;
            _formState.StepState = null;

            if (this._options.HasFlag(FormOptions.PromptInStart))
            {
                await MessageReceived(context, null);
            }
            else
            {
                context.Wait(MessageReceived);
            }
        }

        public async Task MessageReceived(IDialogContext context, IAwaitable<Connector.Message> toBot)
        {
            var toBotText = toBot != null ? (await toBot).Text : null;
            string message = null;
            string prompt = null;
            bool useLastPrompt = false;
            bool requirePrompt = false;
            var next = (_formState.Next == null ? new NextStep() : ActiveSteps(_formState.Next, _state));
            while (prompt == null && (message == null || requirePrompt) && MoveToNext(_state, _formState, next))
            {
                IStep<T> step;
                IEnumerable<TermMatch> matches = null;
                string lastInput = null;
                string feedback = null;
                if (next.Direction == StepDirection.Named && next.Names.Count() > 1)
                {
                    // We need to choose between multiple next steps
                    bool start = (_formState.Next == null);
                    _formState.Next = next;
                    step = new NavigationStep<T>(_form.Steps[_formState.Step].Name, _form, _state, _formState);
                    if (start)
                    {
                        prompt = step.Start(context, _state, _formState);
                    }
                    else
                    {
                        matches = step.Match(context, _state, _formState, toBotText, out lastInput);
                    }
                }
                else
                {
                    // Processing current step
                    step = _form.Steps[_formState.Step];
                    if (_formState.Phase() == StepPhase.Ready)
                    {
                        if (step.Type == StepType.Message)
                        {
                            feedback = step.Start(context, _state, _formState);
                            requirePrompt = true;
                            useLastPrompt = false;
                            next = new NextStep();
                        }
                        else
                        {
                            prompt = step.Start(context, _state, _formState);
                        }
                    }
                    else if (_formState.Phase() == StepPhase.Responding)
                    {
                        matches = step.Match(context, _state, _formState, toBotText, out lastInput);
                    }
                }
                if (matches != null)
                {
                    matches = MatchAnalyzer.Coalesce(matches, lastInput).ToArray();
                    if (MatchAnalyzer.IsFullMatch(lastInput, matches))
                    {
                        var result = await step.ProcessAsync(context, _state, _formState, lastInput, matches);
                        next = result.Next;
                        feedback = result.Feedback;
                        prompt = result.Prompt;

                        // 1) Not completed, not valid -> Not require, last
                        // 2) Completed, feedback -> require, not last
                        requirePrompt = (_formState.Phase() == StepPhase.Completed);
                        useLastPrompt = !requirePrompt;
                    }
                    else
                    {
                        // Filter non-active steps out of command matches
                        var commands =
                            (from command in MatchAnalyzer.Coalesce(_commands.Matches(lastInput), lastInput)
                             where (command.Value is FormCommand
                                 || _form.Fields.Field(command.Value as string).Active(_state))
                             select command).ToArray();
                        if (MatchAnalyzer.IsFullMatch(lastInput, commands))
                        {
                            next = DoCommand(context, _state, _formState, step, commands, out feedback);
                            requirePrompt = false;
                            useLastPrompt = true;
                        }
                        else
                        {
                            if (matches.Count() == 0 && commands.Count() == 0)
                            {
                                // TODO: If we implement fallback, opportunity to call parent dialogs
                                feedback = step.NotUnderstood(context, _state, _formState, lastInput);
                                requirePrompt = false;
                                useLastPrompt = false;
                            }
                            else
                            {
                                // Go with response since it looks possible
                                var bestMatch = MatchAnalyzer.BestMatches(matches, commands);
                                if (bestMatch == 0)
                                {
                                    var result = await step.ProcessAsync(context, _state, _formState, lastInput, matches);
                                    next = result.Next;
                                    feedback = result.Feedback;
                                    prompt = result.Prompt;

                                    requirePrompt = (_formState.Phase() == StepPhase.Completed);
                                    useLastPrompt = !requirePrompt;
                                }
                                else
                                {
                                    next = DoCommand(context, _state, _formState, step, commands, out feedback);
                                    requirePrompt = false;
                                    useLastPrompt = true;
                                }
                            }
                        }
                    }
                }
                next = ActiveSteps(next, _state);
                if (feedback != null)
                {
                    message = (message == null ? feedback : message + "\n\n" + feedback);
                }
            }
            if (next.Direction == StepDirection.Complete || next.Direction == StepDirection.Quit)
            {
                if (next.Direction == StepDirection.Complete)
                {
                    if (message != null)
                    {
                        await context.PostAsync(message);
                    }
                    if (_form.Completion != null)
                    {
                        await _form.Completion(context, _state);
                    }
                    context.Done(_state);
                }
                else if (next.Direction == StepDirection.Quit)
                {
                    throw new OperationCanceledException();
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
            else
            {
                if (message != null)
                {
                    if (requirePrompt)
                    {
                        _formState.LastPrompt = prompt;
                        prompt = message + "\n\n" + prompt;
                    }
                    else if (useLastPrompt)
                    {
                        prompt = message + "\n\n" + _formState.LastPrompt;
                    }
                    else
                    {
                        prompt = message;
                    }
                }
                else
                {
                    _formState.LastPrompt = prompt;
                }

                await context.PostAsync(prompt);
                context.Wait(MessageReceived);
            }
        }

        #endregion

        #region Implementation

        private NextStep ActiveSteps(NextStep next, T state)
        {
            var result = next;
            if (next.Direction == StepDirection.Named)
            {
                var names = (from name in next.Names where _form.Fields.Field(name).Active(state) select name);
                var count = names.Count();
                if (count == 0)
                {
                    result = new NextStep();
                }
                else if (count != result.Names.Count())
                {
                    result = new NextStep(names);
                }
            }
            return result;
        }

        /// <summary>
        /// Find the next step to execute.
        /// </summary>
        /// <param name="state">The current state.</param>
        /// <param name="form">The current form state.</param>
        /// <param name="next">What step to execute next.</param>
        /// <returns>True if can switch to step.</returns>
        private bool MoveToNext(T state, FormState form, NextStep next)
        {
            bool found = false;
            switch (next.Direction)
            {
                case StepDirection.Complete:
                    break;
                case StepDirection.Named:
                    form.StepState = null;
                    if (next.Names.Count() == 0)
                    {
                        goto case StepDirection.Next;
                    }
                    else if (next.Names.Count() == 1)
                    {
                        var name = next.Names.First();
                        var nextStep = -1;
                        for (var i = 0; i < _form.Steps.Count(); ++i)
                        {
                            if (_form.Steps[i].Name == name)
                            {
                                nextStep = i;
                                break;
                            }
                        }
                        if (nextStep == -1)
                        {
                            throw new ArgumentOutOfRangeException("NextStep", "Does not correspond to a field in the form.");
                        }
                        if (_form.Steps[nextStep].Active(state))
                        {
                            var current = _form.Steps[form.Step];
                            form.SetPhase(_form.Fields.Field(current.Name).IsUnknown(state) ? StepPhase.Ready : StepPhase.Completed);
                            form.History.Push(form.Step);
                            form.Step = nextStep;
                            form.SetPhase(StepPhase.Ready);
                            found = true;
                        }
                        else
                        {
                            // If we went to a state which is not active fall through to the next active if any
                            goto case StepDirection.Next;
                        }
                    }
                    else
                    {
                        // Always mark multiple names as found so we can handle the user navigation
                        found = true;
                    }
                    break;
                case StepDirection.Next:
                    var start = form.Step;
                    // Next ready step including current one
                    for (var offset = 0; offset < _form.Steps.Count; ++offset)
                    {
                        form.Step = (start + offset) % _form.Steps.Count;
                        if (offset > 0)
                        {
                            form.StepState = null;
                            form.Next = null;
                        }
                        var step = _form.Steps[form.Step];
                        if ((form.Phase() == StepPhase.Ready || form.Phase() == StepPhase.Responding)
                            && step.Active(state))
                        {
                            if (step.Type == StepType.Confirm)
                            {
                                // Ensure all dependencies have values
                                foreach (var dependency in step.Dependencies)
                                {
                                    var dstep = _form.Step(dependency);
                                    var dstepi = _form.StepIndex(dstep);
                                    if (dstep.Active(state) && form.Phases[dstepi] != StepPhase.Completed)
                                    {
                                        form.Step = dstepi;
                                        break;
                                    }
                                }
                                found = true;
                            }
                            else
                            {
                                found = true;
                            }
                            if (form.Step != start && _form.Steps[start].Type != StepType.Message)
                            {
                                form.History.Push(start);
                            }
                            break;
                        }
                    }
                    if (!found)
                    {
                        next.Direction = StepDirection.Complete;
                    }
                    break;
                case StepDirection.Previous:
                    while (form.History.Count() > 0)
                    {
                        var lastStepIndex = form.History.Pop();
                        var lastStep = _form.Steps[lastStepIndex];
                        if (lastStep.Active(state))
                        {
                            var step = _form.Steps[form.Step];
                            form.SetPhase(step.Field.IsUnknown(state) ? StepPhase.Ready : StepPhase.Completed);
                            form.Step = lastStepIndex;
                            form.SetPhase(StepPhase.Ready);
                            form.StepState = null;
                            form.Next = null;
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        next.Direction = StepDirection.Quit;
                    }
                    break;
                case StepDirection.Quit:
                    break;
                case StepDirection.Reset:
                    form.Reset();
                    // TODO: Should I reset the state as well?
                    // Because we redo phase they can go through everything again but with defaults.
                    found = true;
                    break;
            }
            return found;
        }

        private NextStep DoCommand(IDialogContext context, T state, FormState form, IStep<T> step, IEnumerable<TermMatch> matches, out string feedback)
        {
            // TODO: What if there are more than one command?
            feedback = null;
            var next = new NextStep();
            var value = matches.First().Value;
            if (value is FormCommand)
            {
                switch ((FormCommand)value)
                {
                    case FormCommand.Backup:
                        {
                            next.Direction = step.Back(context, state, form) ? StepDirection.Next : StepDirection.Previous;
                        }
                        break;
                    case FormCommand.Help:
                        {
                            var field = step.Field;
                            var builder = new StringBuilder();
                            foreach (var entry in _form.Configuration.Commands)
                            {
                                builder.Append("* ");
                                builder.AppendLine(entry.Value.Help);
                            }
                            var navigation = new Prompter<T>(field.Template(TemplateUsage.NavigationCommandHelp), _form, null);
                            var active = (from istep in _form.Steps
                                          where istep.Type == StepType.Field && istep.Active(state)
                                          select istep.Field.FieldDescription);
                            var activeList = Language.BuildList(active, navigation.Annotation.Separator, navigation.Annotation.LastSeparator);
                            builder.Append("* ");
                            builder.Append(navigation.Prompt(state, "", activeList));
                            feedback = step.Help(state, form, builder.ToString());
                        }
                        break;
                    case FormCommand.Quit: next.Direction = StepDirection.Quit; break;
                    case FormCommand.Reset: next.Direction = StepDirection.Reset; break;
                    case FormCommand.Status:
                        {
                            var prompt = new Prompt("{*}");
                            feedback = new Prompter<T>(prompt, _form, null).Prompt(state, "");
                        }
                        break;
                }
            }
            else
            {
                var name = value as string;
                var istep = _form.Step(name);
                if (istep != null && istep.Active(state))
                {
                    next = new NextStep(new string[] { name });
                }
            }
            return next;
        }

        #endregion

    }
}

namespace Microsoft.Bot.Builder.Luis
{
    [Serializable]
    public partial class EntityRecommendation
    {
    }

    [Serializable]
    public partial class IntentRecommendation
    {
    }
}
