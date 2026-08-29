using Stagehand.Definitions.Objects;
using Stagehand.Utils;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stagehand.Editor.DefinitionEditors.Objects;

public record class ObjectDefinitionDataTransferFragment(ObjectDefinition ObjectDefinition) : DataTransferFragment;
