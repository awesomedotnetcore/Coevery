﻿using System.Data.Entity.Design.PluralizationServices;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using Coevery.Common.Extensions;
using Coevery.Common.Models;
using Coevery.Perspectives.Services;
using Coevery.Perspectives.ViewModels;
using Coevery.ContentManagement.Aspects;
using Coevery.Core.Contents.Settings;
using Coevery.Core.Navigation.Models;
using Coevery.Core.Settings.Metadata.Records;
using Coevery.Core.Title.Models;
using Coevery.Data;
using Coevery.Localization;
using Coevery.Logging;
using Coevery.UI.Navigation;
using Coevery.Utility;
using Coevery.ContentManagement;

namespace Coevery.Perspectives.Controllers {
    public class SystemAdminController : Controller {
        private readonly IContentDefinitionService _contentDefinitionService;
        private readonly IRepository<ContentTypeDefinitionRecord> _contentTypeDefinitionRepository;
        private readonly IContentManager _contentManager;
        private readonly IContentDefinitionExtension _contentDefinitionExtension;
        private readonly INavigationManager _navigationManager;

        public SystemAdminController(
            ICoeveryServices coeveryServices,
            IContentDefinitionService contentDefinitionService,
            IContentDefinitionExtension contentDefinitionExtension,
            IRepository<ContentTypeDefinitionRecord> contentTypeDefinitionRepository,
            IContentManager contentManager,
            INavigationManager navigationManager) {
            Services = coeveryServices;
            _contentDefinitionExtension = contentDefinitionExtension;
            _contentDefinitionService = contentDefinitionService;
            _contentTypeDefinitionRepository = contentTypeDefinitionRepository;
            _contentManager = contentManager;
            _navigationManager = navigationManager;
            T = NullLocalizer.Instance;
        }

        public ICoeveryServices Services { get; private set; }
        public Localizer T { get; set; }
        public ILogger Logger { get; set; }

        public ActionResult List(string id) {
            return View();
        }


        public ActionResult Create() {
            PerspectiveViewModel model = new PerspectiveViewModel();
            return View(model);
        }

        [HttpPost, ActionName("Create")]
        public ActionResult CreatePOST(PerspectiveViewModel model) {
            var contentItem = _contentManager.New("Menu");
            contentItem.As<TitlePart>().Title = model.Title;
            _contentManager.Create(contentItem, VersionOptions.Draft);
            _contentManager.Publish(contentItem);
            return Json(new {id = contentItem.Id});
        }


        public ActionResult Edit(int id) {
            var contentItem = _contentManager.Get(id, VersionOptions.Latest);
            PerspectiveViewModel model = new PerspectiveViewModel();
            model.Title = contentItem.As<TitlePart>().Title;
            model.Id = contentItem.As<TitlePart>().Id;
            return View(model);
        }

        [HttpPost, ActionName("Edit")]
        public ActionResult EditPOST(int id, PerspectiveViewModel model) {
            var contentItem = _contentManager.Get(id, VersionOptions.DraftRequired);
            contentItem.As<TitlePart>().Title = model.Title;
            _contentManager.Publish(contentItem);
            return new HttpStatusCodeResult(HttpStatusCode.OK);
        }

        public ActionResult Detail(int id) {
            var contentItem = _contentManager.Get(id, VersionOptions.Latest);
            PerspectiveViewModel model = new PerspectiveViewModel();
            model.Title = contentItem.As<TitlePart>().Title;
            model.Id = contentItem.As<TitlePart>().Id;
            return View(model);
        }

        public ActionResult EditNavigationItem(int id) {
            NavigationViewModel model = new NavigationViewModel();

            var metadataTypes = _contentDefinitionService.GetUserDefinedTypes();
            var contentItem = _contentManager.Get(id, VersionOptions.Latest);
            model.EntityName = _contentDefinitionExtension.GetEntityNameFromCollectionName(
                contentItem.As<MenuPart>().MenuText, true);
            var menuId = contentItem.As<MenuPart>().Record.MenuId;
            var perspectiveItem = _contentManager.Get(menuId, VersionOptions.Latest);
            model.Title = perspectiveItem.As<TitlePart>().Title;
            model.IconClass = contentItem.As<ModuleMenuItemPart>().IconClass;
            model.Entities = metadataTypes.Select(item => new SelectListItem {
                Text = item.Name,
                Value = item.Name,
                Selected = item.Name == model.EntityName
            }).ToList();
            model.NavigationId = id;
            model.PrespectiveId = menuId;
            return View(model);
        }

        [HttpPost, ActionName("EditNavigationItem")]
        public ActionResult EditNavigationItemPOST(int perspectiveId, int navigationId, NavigationViewModel model) {
            var pluralContentTypeName = _contentDefinitionExtension.GetEntityNames(model.EntityName);

            var contentItem = _contentManager.Get(navigationId, VersionOptions.DraftRequired);
            
            //Used as the module id of front end
            contentItem.As<MenuPart>().MenuText = pluralContentTypeName.CollectionDisplayName;
            contentItem.As<ModuleMenuItemPart>().ContentTypeDefinitionRecord = _contentTypeDefinitionRepository.Table.FirstOrDefault(x => x.Name == model.EntityName);
            contentItem.As<ModuleMenuItemPart>().IconClass = model.IconClass;
            //contentItem.As<MenuItemPart>().FeatureId = "Coevery." + pluralContentTypeName;
            _contentManager.Publish(contentItem);

            return new HttpStatusCodeResult(HttpStatusCode.OK);
        }

        public ActionResult CreateNavigationItem(int id) {
            NavigationViewModel model = new NavigationViewModel();
            var metadataTypes = _contentDefinitionService.GetUserDefinedTypes();
            var perspectiveItem = _contentManager.Get(id, VersionOptions.Latest);
            model.Title = perspectiveItem.As<TitlePart>().Title;
            model.Entities = metadataTypes.Select(item => new SelectListItem {
                Text = item.Name,
                Value = item.Name,
                Selected = item.Name == model.EntityName
            }).ToList();
            model.NavigationId = 0;
            model.PrespectiveId = id;
            return View(model);
        }

        [HttpPost, ActionName("CreateNavigationItem")]
        public ActionResult CreateNavigationItemPOST(int perspectiveId, int navigationId, NavigationViewModel model) {
            var pluralContentTypeName = _contentDefinitionExtension.GetEntityNames(model.EntityName);

            if (string.IsNullOrWhiteSpace(model.IconClass)) {
                Response.StatusCode = (int) HttpStatusCode.BadRequest;
                return Content(T("Icon is required.").ToString());
            }

            //add
            var moduleMenuPart = Services.ContentManager.New<MenuPart>("ModuleMenuItem");

            // load the menu
            var menu = Services.ContentManager.Get(perspectiveId);

            moduleMenuPart.MenuText = pluralContentTypeName.CollectionDisplayName;
            moduleMenuPart.MenuPosition = Position.GetNext(_navigationManager.BuildMenu(menu));
            moduleMenuPart.Menu = menu;
            moduleMenuPart.MenuText = pluralContentTypeName.CollectionDisplayName;

            moduleMenuPart.As<ModuleMenuItemPart>().ContentTypeDefinitionRecord = _contentTypeDefinitionRepository.Table.FirstOrDefault(x => x.Name == model.EntityName);
            moduleMenuPart.As<ModuleMenuItemPart>().IconClass = model.IconClass;
            //menuPart.As<MenuItemPart>().FeatureId = "Coevery." + pluralContentTypeName;
            Services.ContentManager.Create(moduleMenuPart);
            if (!moduleMenuPart.ContentItem.Has<IPublishingControlAspect>() && !moduleMenuPart.ContentItem.TypeDefinition.Settings.GetModel<ContentTypeSettings>().Draftable) {
                _contentManager.Publish(moduleMenuPart.ContentItem);
            }
            return Json(new {id = moduleMenuPart.ContentItem.Id});
        }
    }
}