﻿using Microsoft.Templates.Wizard.Dialog;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Templates.Wizard.Vs;
using Microsoft.Templates.Core;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Edge.Template;
using Microsoft.Templates.Core.Locations;
using Microsoft.Templates.Wizard.Resources;
using Microsoft.Templates.Wizard.Steps;
using Microsoft.Templates.Wizard.Host;
using Newtonsoft.Json;
using Microsoft.Templates.Wizard.PostActions;
using Microsoft.Templates.Core.Diagnostics;
using Microsoft.Templates.Core.Extensions;

namespace Microsoft.Templates.Wizard
{
    public class GenerationWizard
    {
        TemplatesRepository _templatesRepository;
        IVisualStudioShell _vsShell;
        SolutionInfo _vsSolutionInfo;

        private TemplateConfig[] _selectedTemplates;

        public GenerationWizard(IVisualStudioShell vsShell, SolutionInfo solutionInfo) 
            : this(vsShell, solutionInfo, new TemplatesRepository(new CdnTemplatesLocation()))
        {
        }

        public GenerationWizard(IVisualStudioShell currentVsShell, SolutionInfo solutionInfo, TemplatesRepository repository)
        {
            _vsShell = currentVsShell;
            _vsSolutionInfo = solutionInfo;
            _templatesRepository = repository;
            _templatesRepository.Sync();
        }

        public void AddProjectInit()
        {
            _vsShell.ShowStatusBarMessage(StringRes.UIAddProjectAdding);

            var steps = new WizardSteps();

            steps.Add<Steps.ProjectsStep.ProjectsStepPage>();
            steps.Add<Steps.SummaryStep.SummaryStepPage>();

            var host = new WizardHost(_templatesRepository, steps);
            var result = host.ShowDialog();

            if (result.HasValue && result.Value)
            {
                _selectedTemplates = host.Result.ToArray();
            }
            else
            {
                _selectedTemplates = null;
            }
        }
        public void AddProjectFinish()
        {
            try
            {
                if (_selectedTemplates != null)
                {
                    //TODO: THIS SHOULD GENERATE MORE THAN ONE TEMPLATE
                    var targetTemplate = _selectedTemplates.First().Info;

                    AppHealth.Current.Info.TrackAsync($"Selected template: {targetTemplate.DefaultName}").FireAndForget();

                    _vsShell.ShowStatusBarMessage(StringRes.UIAddProjectGenerating);

                    GenerateProject(targetTemplate, _vsSolutionInfo, _vsShell);

                    AppHealth.Current.Verbose.TrackAsync($"Template generated successfully.").FireAndForget();

                    _vsShell.SetSolutionVsCategory(_vsSolutionInfo.TemplateCategory);

                    AppHealth.Current.Verbose.TrackAsync($"Category {_vsSolutionInfo.TemplateCategory} established in the solution.").FireAndForget();

                    ShowReadMe(targetTemplate);

                    AppHealth.Current.Verbose.TrackAsync($"Readme shown.").FireAndForget();

                    _vsShell.ShowStatusBarMessage(StringRes.UIProjectSuccessfullyCreated);
                }
            }
            catch (Exception ex)
            {
                ShowException(StringRes.UIErrorProjectCantBeGenerated, ex);
            }
        }


        public void AddPageToActiveProject()
        {
            //TODO: THIS SHOULD BE A UNIQUE METHOD??
            if (_vsShell.GetActiveProjectName() != "")
            {
                _vsShell.ShowStatusBarMessage(StringRes.UIAddPage);

                string suggestedNamespace = _vsShell.GetSelectedItemDefaultNamespace();

                var steps = new WizardSteps();

                steps.Add<Steps.PagesStep.PagesStepPage>();
                steps.Add<Steps.SummaryStep.SummaryStepPage>();

                var host = new WizardHost(_templatesRepository, steps);
                var result = host.ShowDialog();

                if (result.HasValue && result.Value)
                {
                    var pageTemplateConfig = host.Result.First();
                    var pageName = pageTemplateConfig.Parameters["ItemName"];

                    string relativeItemPath = _vsShell.GetSelectedItemPath(true);

                    GeneratePage(pageTemplateConfig.Info, _vsShell.GetActiveProjectName(), _vsShell.GetActiveProjectPath(), suggestedNamespace, pageName, relativeItemPath, _vsShell);
                    _vsShell.ShowStatusBarMessage(StringRes.UIPageAddedPattern.UseParams(pageName));
                }
                else
                {
                    _vsShell.ShowStatusBarMessage(StringRes.UIActionCancelled);
                }
            }
            else
            {
                MessageBox.Show(StringRes.UINoActiveProjectMessage, StringRes.UIMessageBoxTitlePattern.UseParams(StringRes.AddPageAction), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        public void AddFeatureToActiveProject()
        {
            AddFeaturesResult result = ShowAddFeaturesDialog(_vsShell.GetActiveProjectName(), _vsSolutionInfo.TemplateCategory);
            if (result != null)
            {
                //TODO: Generate Feature Code

                //TODO: Include generated code
            }
            else
            {
                _vsShell.ShowStatusBarMessage(StringRes.UIActionCancelled);
            }
        }

        private AddProjectResult ShowAddProjectDialog(string targetProjectName, string vsTemplateCategory)
        {
            try {
                AddNewProject addProjectDialog = new AddNewProject(targetProjectName, vsTemplateCategory, _templatesRepository, AddProjectSteps.SelectProject);
                var result = addProjectDialog.ShowDialog();

                return (result.HasValue && result.Value ? addProjectDialog.ResultInfo : null);
            }
            catch (Exception ex)
            {
                ShowException(StringRes.UIErrorProjectCantBeGenerated, ex);
                return null;
            }
        }

        private AddFeaturesResult ShowAddFeaturesDialog(string targetProjectName, string vsTemplateCategory)
        {
            try
            {
                MessageBox.Show("Not implemented!", StringRes.UIMessageBoxTitlePattern.UseParams(StringRes.AddFeatureAction), MessageBoxButton.OK, MessageBoxImage.Asterisk);
                return null;
            }
            catch (Exception ex)
            {
                ShowException(StringRes.UIErrorFeatureCantBeAdded, ex);
                return null;
            }
        }

        private AddPageResult ShowAddPageDialog(string targetProjectName, string vsTemplateCategory, string suggestedNamespace)
        {
            try
            {
                AddPage addPageDialog = new AddPage(targetProjectName, vsTemplateCategory, suggestedNamespace, _templatesRepository);
                var result = addPageDialog.ShowDialog();
                return (result.HasValue && result.Value ? addPageDialog.ResultInfo : null);
            }
            catch (Exception ex)
            {
                ShowException(StringRes.UIErrorPageCantBeAdded, ex);
                return null;
            }
        }

        private static void GenerateProject(ITemplateInfo template, SolutionInfo solutionInfo, IVisualStudioShell vsShell)
        {
			var userName = Environment.UserName;
			var genParams = new Dictionary<string, string>
			{
				{ "UserName", userName }
			};

			var result = TemplateCreator.InstantiateAsync(template, solutionInfo.Name, null, solutionInfo.Directory, genParams, true).Result;

            if (result.Status == CreationResultStatus.CreateSucceeded && result.PrimaryOutputs != null)
            {
                foreach (var output in result.PrimaryOutputs)
                {
                    if (!string.IsNullOrWhiteSpace(output.Path))
                    {
                        var projectPath = Path.GetFullPath(Path.Combine(solutionInfo.Directory, output.Path));
                        vsShell.AddProjectToSolution(projectPath); 
                    }
                } 
            }

			//Execute post actions
			var postActionResults = ExecutePostActions(template, new ExecutionContext() {
				ProjectName = solutionInfo.Name,
				ProjectPath = Path.Combine(solutionInfo.Directory, solutionInfo.Name),
				PagePath = "",
				UserName = userName
			});

			//TODO: Show Post Action Info
		}

		private static IEnumerable<PostActionExecutionResult> ExecutePostActions(ITemplateInfo template, ExecutionContext executionContext)
		{
			//Get post actions from template
			var postActions = PostActionCreator.GetPostActions(template);

			//Execute post action
			var postActionResults = new List<PostActionExecutionResult>();

			foreach (var postAction in postActions)
			{
				var postActionResult = postAction.Execute(executionContext);
				postActionResults.Add(postActionResult);
			}

			return postActionResults;
		}

		private static void GeneratePage(ITemplateInfo template, string projectName, string projectPath, string pageNamespace, string pageName, string pageRelativePath, IVisualStudioShell vsShell)
        {
            var genParams = new Dictionary<string, string>
            {
                { "PageNamespace", pageNamespace }
            };

            string generationPath = Path.Combine(projectPath, pageRelativePath);
            var result = TemplateCreator.InstantiateAsync(template, pageName, null, generationPath, genParams, true).Result;
            
            //TODO: Control overwrites! What happend if the generated content already exits.

            if (result.Status == CreationResultStatus.CreateSucceeded && result.PrimaryOutputs != null)
            {
                foreach (var output in result.PrimaryOutputs)
                {
                    if (!string.IsNullOrWhiteSpace(output.Path))
                    {
                        var itemPath = Path.GetFullPath(Path.Combine(generationPath, output.Path));
                        vsShell.AddItemToActiveProject(itemPath);
                    }
                }
            }

			//Execute post action
			var postActionResults = ExecutePostActions(template, new ExecutionContext()
			{
				ProjectName = projectName,
				ProjectPath = projectPath,
				PagePath = Path.Combine(generationPath, pageName)
			});

			//TODO: Show Post Action Info

			//TODO: Show Project Information
		}

		private void ShowReadMe(ITemplateInfo template)
        {
            //TODO: GoTo formula Readme / Help page
            _vsShell.Navigate("https://github.com/Microsoft/UWPCommunityTemplates/tree/vnext");
        }

        private void ShowException(string message, Exception ex)
        {
            //TODO: Create a dialog to show exceptionn. Show detailed info depending on setting value;
            ErrorMessageDialog.Show(message, StringRes.UIMessageBoxTitlePattern.UseParams(StringRes.UnexpectedError), ex.ToString(), MessageBoxImage.Error);
            AppHealth.Current.Exception.TrackAsync(ex, message).FireAndForget();
        }
    }
}