﻿using System;
using System.Collections.Generic;
using System.Linq;
using Coevery.Common.Extensions;
using Coevery.Entities.Extensions;
using Coevery.Entities.ViewModels;
using Coevery;
using Coevery.ContentManagement;
using Coevery.ContentManagement.Drivers;
using Coevery.ContentManagement.MetaData;
using Coevery.ContentManagement.MetaData.Models;
using Coevery.ContentManagement.ViewModels;
using Coevery.Core.Contents.Extensions;
using Coevery.Localization;
using Coevery.Utility.Extensions;
using IContentDefinitionEditorEvents = Coevery.Entities.Settings.IContentDefinitionEditorEvents;

namespace Coevery.Entities.Services {
    public class ContentDefinitionService : IContentDefinitionService {
        private readonly IContentDefinitionManager _contentDefinitionManager;
        private readonly IContentDefinitionExtension _contentDefinitionExtension;
        private readonly IContentDefinitionEditorEvents _contentDefinitionEditorEvents;

        public ContentDefinitionService(
            ICoeveryServices services,
            IContentDefinitionManager contentDefinitionManager,
            IContentDefinitionExtension contentDefinitionExtension,
            IContentDefinitionEditorEvents contentDefinitionEditorEvents) {
            Services = services;
            _contentDefinitionManager = contentDefinitionManager;
            _contentDefinitionEditorEvents = contentDefinitionEditorEvents;
            _contentDefinitionExtension = contentDefinitionExtension;
            T = NullLocalizer.Instance;
        }

        public ICoeveryServices Services { get; set; }
        public Localizer T { get; set; }

        public IEnumerable<EditTypeViewModel> GetTypes() {
            return _contentDefinitionManager.ListTypeDefinitions().Select(ctd => new EditTypeViewModel(ctd)).OrderBy(m => m.DisplayName);
        }

        public IEnumerable<EditTypeViewModel> GetUserDefinedTypes() {
            return _contentDefinitionExtension.ListUserDefinedTypeDefinitions().Select(ctd => new EditTypeViewModel(ctd)).OrderBy(m => m.DisplayName);
        }

        public EditTypeViewModel GetType(string name) {
            var contentTypeDefinition = _contentDefinitionManager.GetTypeDefinition(name);

            if (contentTypeDefinition == null)
                contentTypeDefinition = new ContentTypeDefinition(name, name);

            var viewModel = new EditTypeViewModel(contentTypeDefinition) {
                Templates = _contentDefinitionEditorEvents.TypeEditor(contentTypeDefinition)
            };

            foreach (var part in viewModel.Parts) {
                part.Templates = _contentDefinitionEditorEvents.TypePartEditor(part._Definition);
                foreach (var field in part.PartDefinition.Fields)
                    field.Templates = _contentDefinitionEditorEvents.PartFieldEditor(field._Definition);
            }

            if (viewModel.Fields.Any()) {
                foreach (var field in viewModel.Fields)
                    field.Templates = _contentDefinitionEditorEvents.PartFieldEditor(field._Definition);
            }
            return viewModel;
        }

        public ContentTypeDefinition AddType(string name, string displayName) {
            if (String.IsNullOrWhiteSpace(displayName)) {
                throw new ArgumentException("displayName");
            }

            if (String.IsNullOrWhiteSpace(name)) {
                name = GenerateContentTypeNameFromDisplayName(displayName);
            }
            else {
                if (!name[0].IsLetter()) {
                    throw new ArgumentException("Content type name must start with a letter", "name");
                }
            }

            while (_contentDefinitionManager.GetTypeDefinition(name) != null)
                name = VersionName(name);

            var contentTypeDefinition = new ContentTypeDefinition(name, displayName);
            _contentDefinitionManager.StoreTypeDefinition(contentTypeDefinition);
            _contentDefinitionManager.AlterTypeDefinition(name,
                                                          cfg => cfg.Creatable().Draftable());

            return contentTypeDefinition;
        }

        public void AlterType(EditTypeViewModel typeViewModel, IUpdateModel updateModel) {
            var updater = new Updater(updateModel);
            _contentDefinitionManager.AlterTypeDefinition(typeViewModel.Name, typeBuilder => {
                typeBuilder.DisplayedAs(typeViewModel.DisplayName);

                // allow extensions to alter type configuration
                typeViewModel.Templates = _contentDefinitionEditorEvents.TypeEditorUpdate(typeBuilder, updater);

                foreach (var part in typeViewModel.Parts) {
                    var partViewModel = part;

                    // enable updater to be aware of changing part prefix
                    updater._prefix = secondHalf => string.Format("{0}.{1}", partViewModel.Prefix, secondHalf);

                    // allow extensions to alter typePart configuration
                    typeBuilder.WithPart(partViewModel.PartDefinition.Name, typePartBuilder => {
                        partViewModel.Templates = _contentDefinitionEditorEvents.TypePartEditorUpdate(typePartBuilder, updater);
                    });

                    if (!partViewModel.PartDefinition.Fields.Any())
                        continue;

                    _contentDefinitionManager.AlterPartDefinition(partViewModel.PartDefinition.Name, partBuilder => {
                        var fieldFirstHalf = string.Format("{0}.{1}", partViewModel.Prefix, partViewModel.PartDefinition.Prefix);
                        foreach (var field in partViewModel.PartDefinition.Fields) {
                            var fieldViewModel = field;

                            // enable updater to be aware of changing field prefix
                            updater._prefix = secondHalf =>
                                              string.Format("{0}.{1}.{2}", fieldFirstHalf, fieldViewModel.Prefix, secondHalf);
                            // allow extensions to alter partField configuration
                            partBuilder.WithField(fieldViewModel.Name, partFieldBuilder => {
                                fieldViewModel.Templates = _contentDefinitionEditorEvents.PartFieldEditorUpdate(partFieldBuilder, updater);
                            });
                        }
                    });
                }

                if (typeViewModel.Fields.Any()) {
                    _contentDefinitionManager.AlterPartDefinition(typeViewModel.Name.ToPartName(), partBuilder => {
                        foreach (var field in typeViewModel.Fields) {
                            var fieldViewModel = field;

                            // enable updater to be aware of changing field prefix
                            updater._prefix = secondHalf =>
                                              string.Format("{0}.{1}", fieldViewModel.Prefix, secondHalf);

                            // allow extensions to alter partField configuration
                            partBuilder.WithField(fieldViewModel.Name, partFieldBuilder => {
                                fieldViewModel.Templates = _contentDefinitionEditorEvents.PartFieldEditorUpdate(partFieldBuilder, updater);
                            });
                        }
                    });
                }
            });
        }

        public void AlterField(string name, string fieldName, IUpdateModel updateModel) {
            var updater = new Updater(updateModel);
            updater._prefix = secondHalf => secondHalf;
            _contentDefinitionManager
                .AlterPartDefinition(name,
                                     partBuilder => partBuilder.WithField(fieldName,
                                                                          partFieldBuilder => _contentDefinitionEditorEvents.PartFieldEditorUpdate(partFieldBuilder, updater)));
        }

        public void RemoveType(string name, bool deleteContent) {

            // first remove all attached parts
            var typeDefinition = _contentDefinitionManager.GetTypeDefinition(name);
            var partDefinitions = typeDefinition.Parts.ToArray();
            foreach (var partDefinition in partDefinitions) {
                RemovePartFromType(partDefinition.PartDefinition.Name, name);

                // delete the part if it's its own part
                if (string.Equals(partDefinition.PartDefinition.Name,name.ToPartName())) {
                    RemovePart(name.ToPartName());
                }
            }

            _contentDefinitionManager.DeleteTypeDefinition(name);

            // delete all content items (but keep versions)
            if (deleteContent) {
                var contentItems = Services.ContentManager.Query(name).List();
                foreach (var contentItem in contentItems) {
                    Services.ContentManager.Remove(contentItem);
                }
            }

        }

        public void AddPartToType(string partName, string typeName) {
            _contentDefinitionManager.AlterTypeDefinition(typeName, typeBuilder => typeBuilder.WithPart(partName));
        }

        public void RemovePartFromType(string partName, string typeName) {
            _contentDefinitionManager.AlterTypeDefinition(typeName, typeBuilder => typeBuilder.RemovePart(partName));
        }

        public EditPartViewModel GetPart(string name) {
            var contentPartDefinition = _contentDefinitionManager.GetPartDefinition(name);

            if (contentPartDefinition == null)
                return null;

            var viewModel = new EditPartViewModel(contentPartDefinition) {
                Templates = _contentDefinitionEditorEvents.PartEditor(contentPartDefinition)
            };

            return viewModel;
        }

        public EditPartViewModel AddPart(CreatePartViewModel partViewModel) {
            var name = partViewModel.Name;

            if (_contentDefinitionManager.GetPartDefinition(name) != null)
                throw new CoeveryException(T("Cannot add part named '{0}'. It already exists.", name));

            if (!String.IsNullOrEmpty(name)) {
                _contentDefinitionManager.AlterPartDefinition(name, builder => builder.Attachable());
                return new EditPartViewModel(_contentDefinitionManager.GetPartDefinition(name));
            }

            return null;
        }

        public void AlterPart(EditPartViewModel partViewModel, IUpdateModel updateModel) {
            var updater = new Updater(updateModel);
            _contentDefinitionManager.AlterPartDefinition(partViewModel.Name, partBuilder => {
                partViewModel.Templates = _contentDefinitionEditorEvents.PartEditorUpdate(partBuilder, updater);
            });
        }

        public void RemovePart(string name) {
            var partDefinition = _contentDefinitionManager.GetPartDefinition(name);
            var fieldDefinitions = partDefinition.Fields.ToArray();
            foreach (var fieldDefinition in fieldDefinitions) {
                RemoveFieldFromPart(fieldDefinition.Name, name);
            }

            _contentDefinitionManager.DeletePartDefinition(name);
        }

        public IEnumerable<TemplateViewModel> GetFields() {
            return _contentDefinitionEditorEvents.FieldTypeDescriptor();
        }

        public void AddFieldToPart(string fieldName, string fieldTypeName, string partName) {
            AddFieldToPart(fieldName, fieldName, fieldTypeName, partName);
        }

        public void AddFieldToPart(string fieldName, string displayName, string fieldTypeName, string partName) {
            fieldName = fieldName.ToSafeName();
            if (string.IsNullOrEmpty(fieldName)) {
                throw new CoeveryException(T("Fields must have a name containing no spaces or symbols."));
            }
            _contentDefinitionManager
                .AlterPartDefinition(partName,
                                     partBuilder => partBuilder.WithField(fieldName,
                                                                          fieldBuilder => fieldBuilder
                                                                                              .OfType(fieldTypeName)
                                                                                              .WithDisplayName(displayName)
                                                                                              .WithSetting("Storage", "Part")));
        }

        public void RemoveFieldFromPart(string fieldName, string partName) {
            _contentDefinitionManager.AlterPartDefinition(partName, typeBuilder => typeBuilder.RemoveField(fieldName));
        }

        public string GenerateContentTypeNameFromDisplayName(string displayName) {
            displayName = displayName.ToSafeName();

            while (_contentDefinitionManager.GetTypeDefinition(displayName) != null)
                displayName = VersionName(displayName);

            return displayName;
        }

        public string GenerateFieldNameFromDisplayName(string partName, string displayName) {
            IEnumerable<ContentPartFieldDefinition> fieldDefinitions;

            var part = _contentDefinitionManager.GetPartDefinition(partName.ToPartName());
            displayName = displayName.ToSafeName();

            if (part == null) {
                var type = _contentDefinitionManager.GetTypeDefinition(partName);

                if (type == null) {
                    throw new ArgumentException("The entity doesn't exist: " + partName);
                }

                var typePart = type.Parts.FirstOrDefault(x => x.PartDefinition.Name == partName.ToPartName());

                // id passed in might be that of a type w/ no implicit field
                if (typePart == null) {
                    return displayName;
                }
                else {
                    fieldDefinitions = typePart.PartDefinition.Fields.ToArray();
                }

            }
            else {
                fieldDefinitions = part.Fields.ToArray();
            }

            while (fieldDefinitions.Any(x => String.Equals(displayName.Trim(), x.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                displayName = VersionName(displayName);

            return displayName;
        }

        private static string VersionName(string name) {
            int version;
            var nameParts = name.Split(new[] {'_'}, StringSplitOptions.RemoveEmptyEntries);

            if (nameParts.Length > 1 && int.TryParse(nameParts.Last(), out version)) {
                version = version > 0 ? ++version : 2;
                //this could unintentionally chomp something that looks like a version
                name = string.Join("_", nameParts.Take(nameParts.Length - 1));
            }
            else {
                version = 2;
            }

            return string.Format("{0}_{1}", name, version);
        }

        public class Updater : IUpdateModel {
            private readonly IUpdateModel _thunk;

            public Updater(IUpdateModel thunk) {
                _thunk = thunk;
            }

            public Func<string, string> _prefix = x => x;

            public bool TryUpdateModel<TModel>(TModel model, string prefix, string[] includeProperties, string[] excludeProperties) where TModel : class {
                return _thunk.TryUpdateModel(model, _prefix(prefix), includeProperties, excludeProperties);
            }

            public void AddModelError(string key, LocalizedString errorMessage) {
                _thunk.AddModelError(_prefix(key), errorMessage);
            }
        }
    }
}