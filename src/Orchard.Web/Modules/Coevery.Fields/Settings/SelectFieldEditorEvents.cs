﻿using System;
using System.Collections.Generic;
using Coevery.Fields.Services;
using Orchard.ContentManagement;
using Orchard.ContentManagement.MetaData.Builders;
using Orchard.ContentManagement.MetaData.Models;
using Orchard.ContentManagement.ViewModels;
using Orchard.Localization;

namespace Coevery.Fields.Settings {
    public class SelectFieldListModeEvents : FieldEditorEvents {
        private readonly IOptionItemService _optionItemService;
        private readonly Localizer _t = NullLocalizer.Instance;

        public SelectFieldListModeEvents(IOptionItemService optionItemService) {
            _optionItemService = optionItemService;
        }

        public override IEnumerable<TemplateViewModel> PartFieldEditor(ContentPartFieldDefinition definition) {
            if (definition.FieldDefinition.Name == "SelectField" ||
                definition.FieldDefinition.Name == "SelectFieldCreate") {
                var model = definition.Settings.GetModel<SelectFieldSettings>();
                yield return DefinitionTemplate(model);
            }
        }

        public override IEnumerable<TemplateViewModel> PartFieldEditorUpdate(ContentPartFieldDefinitionBuilder builder, IUpdateModel updateModel) {
            if (builder.FieldType != "SelectField") {
                yield break;
            }

            var model = new SelectFieldSettings();
            if (updateModel.TryUpdateModel(model, "SelectFieldSettings", null, null)) {
                var itemCount = _optionItemService.GetItemCountForField(model.FieldSettingId);
                if (!model.CheckValid(updateModel, _t, itemCount, false)) {
                    yield break;
                }
                UpdateSettings(model, builder, "SelectFieldSettings");
                builder.WithSetting("SelectFieldSettings.DisplayLines", model.DisplayLines.ToString());
                builder.WithSetting("SelectFieldSettings.DisplayOption", model.DisplayOption.ToString());
                builder.WithSetting("SelectFieldSettings.SelectCount", model.SelectCount.ToString());
            }

            yield return DefinitionTemplate(model);
        }

        public override IEnumerable<TemplateViewModel> PartFieldEditorCreate(ContentPartFieldDefinitionBuilder builder, string typeName, IUpdateModel updateModel) {
            if (builder.FieldType != "SelectField") {
                yield break;
            }

            var model = new SelectFieldSettings();
            if (updateModel.TryUpdateModel(model, "SelectFieldSettings", null, null)) {
                //Basic Validation, should be replaced later
                if (string.IsNullOrWhiteSpace(model.LabelsStr)) {
                    updateModel.AddModelError("SelectSettings", _t("The LabelsStr is invalid."));
                    yield break;
                }

                var labels = model.LabelsStr.Split(SelectFieldSettings.LabelSeperator, StringSplitOptions.RemoveEmptyEntries);
                if (!model.CheckValid(updateModel, _t, labels.Length, true)) {
                    yield break;
                }
                model.FieldSettingId = _optionItemService.InitializeField(typeName,builder.Name,labels,model.DefaultValue);
                if (model.FieldSettingId < 0) {
                    updateModel.AddModelError("SelectSettings", _t("Create option items faild."));
                    yield break;
                }

                UpdateSettings(model, builder, "SelectFieldSettings");
                builder.WithSetting("SelectFieldSettings.DisplayLines", model.DisplayLines.ToString());
                builder.WithSetting("SelectFieldSettings.DisplayOption", model.DisplayOption.ToString());
                builder.WithSetting("SelectFieldSettings.FieldSettingId", model.FieldSettingId.ToString());
                builder.WithSetting("SelectFieldSettings.SelectCount", model.SelectCount.ToString());
            }

            yield return DefinitionTemplate(model);
        }
    }
}