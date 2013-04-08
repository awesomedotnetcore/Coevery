﻿using System;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using Coevery.Metadata.Services;
using Coevery.Metadata.ViewModels;
using Orchard.ContentManagement.MetaData.Models;
using Orchard;
using Orchard.Localization;
using Orchard.UI.Notify;
using Orchard.Utility.Extensions;

namespace Coevery.Metadata.Controllers
{
    public class FieldViewTemplateController : Controller
    {
        private readonly IContentDefinitionService _contentDefinitionService;

        public FieldViewTemplateController(
            IOrchardServices orchardServices,
            IContentDefinitionService contentDefinitionService) {
            Services = orchardServices;
            _contentDefinitionService = contentDefinitionService;
            T = NullLocalizer.Instance;
        }

        public IOrchardServices Services { get; private set; }
        public Localizer T { get; set; }

        public ActionResult List()
        {
            return View();
        }

        public ActionResult Detail()
        {
            return View();
        }

        public ActionResult Create(string id) {
            if (!Services.Authorizer.Authorize(Permissions.EditContentTypes, T("Not allowed to edit a content part.")))
                return new HttpUnauthorizedResult();

            var partViewModel = _contentDefinitionService.GetPart(id);

            if (partViewModel == null)
            {
                //id passed in might be that of a type w/ no implicit field
                var typeViewModel = _contentDefinitionService.GetType(id);
                if (typeViewModel != null)
                    partViewModel = new EditPartViewModel(new ContentPartDefinition(id));
                else
                    return HttpNotFound();
            }

            var viewModel = new AddFieldViewModel
            {
                Part = partViewModel,
                Fields = _contentDefinitionService.GetFields().OrderBy(x => x.FieldTypeName)
            };

            return View(viewModel);
        }

        [HttpPost, ActionName("Create")]
        public ActionResult CreatePost(AddFieldViewModel viewModel, string id)
        {
            if (!Services.Authorizer.Authorize(Permissions.EditContentTypes, T("Not allowed to edit a content part.")))
                return new HttpUnauthorizedResult();

            var partViewModel = _contentDefinitionService.GetPart(id);
            var typeViewModel = _contentDefinitionService.GetType(id);
            if (partViewModel == null)
            {
                // id passed in might be that of a type w/ no implicit field
                if (typeViewModel != null)
                {
                    partViewModel = new EditPartViewModel { Name = typeViewModel.Name };
                    _contentDefinitionService.AddPart(new CreatePartViewModel { Name = partViewModel.Name });
                    _contentDefinitionService.AddPartToType(partViewModel.Name, typeViewModel.Name);
                }
                else
                {
                    return HttpNotFound();
                }
            }

            viewModel.DisplayName = viewModel.DisplayName ?? String.Empty;
            viewModel.DisplayName = viewModel.DisplayName.Trim();
            viewModel.Name = viewModel.Name ?? String.Empty;

            if (String.IsNullOrWhiteSpace(viewModel.DisplayName))
            {
                ModelState.AddModelError("DisplayName", T("The Display Name name can't be empty.").ToString());
            }

            if (String.IsNullOrWhiteSpace(viewModel.Name))
            {
                ModelState.AddModelError("Name", T("The Technical Name can't be empty.").ToString());
            }

            if (_contentDefinitionService.GetPart(partViewModel.Name).Fields.Any(t => String.Equals(t.Name.Trim(), viewModel.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError("Name", T("A field with the same name already exists.").ToString());
            }

            if (!String.IsNullOrWhiteSpace(viewModel.Name) && !viewModel.Name[0].IsLetter())
            {
                ModelState.AddModelError("Name", T("The technical name must start with a letter.").ToString());
            }

            if (!String.Equals(viewModel.Name, viewModel.Name.ToSafeName(), StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("Name", T("The technical name contains invalid characters.").ToString());
            }

            if (_contentDefinitionService.GetPart(partViewModel.Name).Fields.Any(t => String.Equals(t.DisplayName.Trim(), Convert.ToString(viewModel.DisplayName).Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                ModelState.AddModelError("DisplayName", T("A field with the same Display Name already exists.").ToString());
            }

            if (!ModelState.IsValid)
            {
                viewModel.Part = partViewModel;
                viewModel.Fields = _contentDefinitionService.GetFields();

                Services.TransactionManager.Cancel();

                return View(viewModel);
            }

            try
            {
                _contentDefinitionService.AddFieldToPart(viewModel.Name, viewModel.DisplayName, viewModel.FieldTypeName, partViewModel.Name);
            }
            catch (Exception ex)
            {
                Services.Notifier.Information(T("The \"{0}\" field was not added. {1}", viewModel.DisplayName, ex.Message));
                Services.TransactionManager.Cancel();
                return Create(id);
            }

            Services.Notifier.Information(T("The \"{0}\" field has been added.", viewModel.DisplayName));

            return new HttpStatusCodeResult(HttpStatusCode.OK);
        }
    }
}
