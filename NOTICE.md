# DeltaZulu Kusto Query Language Dependency Exception

DeltaZulu Agent and DeltaZulu Platform are licensed under the GNU Affero General Public License version 3.0, with the additional permission described in this file.

## Purpose

DeltaZulu provides KQL-compatible query authoring and translation functionality. For that purpose, DeltaZulu may depend on Microsoft-published Kusto Query Language components, including the KQL parser, semantic model, language schemas, and base language libraries.

The upstream Kusto Query Language source code and base language libraries are published by Microsoft under the Apache License 2.0. Some distributed package forms, including NuGet packages, may also present Microsoft Azure Data Explorer SDK license terms. This exception exists to make the intended licensing boundary explicit.

## Additional permission

As the copyright holder of DeltaZulu, I grant permission to compile, link, combine, execute, and distribute DeltaZulu with unmodified Microsoft-published Kusto Query Language components, including `Microsoft.Azure.Kusto.Language`, when those components are used only as external third-party dependencies through their public APIs.

This permission applies only to the use of those external Kusto Query Language components as dependencies. It does not relicense Microsoft code. Microsoft-published Kusto Query Language components remain governed by their own applicable license terms, including Apache License 2.0 where applicable and any Microsoft package license terms that apply to the specific distributed artifact.

## Scope

This exception permits DeltaZulu to use external Kusto Query Language components for language parsing, semantic analysis, schema-aware authoring, query validation, and related KQL language-support functionality.

This exception does not permit:

1. copying Microsoft source code into DeltaZulu source files;
2. redistributing modified Microsoft Kusto Query Language packages as part of DeltaZulu;
3. treating Microsoft Kusto Query Language components as AGPL-licensed DeltaZulu code;
4. imposing AGPL-3.0 source-disclosure or modification-right obligations on Microsoft code;
5. using Microsoft trademarks, product names, or trade dress in a way that suggests Microsoft endorsement, sponsorship, or affiliation; or
6. adding Azure Data Explorer data-plane, ingestion, management, or service connector functionality to DeltaZulu under this exception.

## Corresponding Source clarification

For purposes of DeltaZulu’s AGPL-3.0 license, Microsoft-published Kusto Query Language components used under this exception are not part of the DeltaZulu covered work.

The source code of `Microsoft.Azure.Kusto.Language` and related external Microsoft Kusto Query Language components is not part of DeltaZulu’s Corresponding Source merely because DeltaZulu references those components as third-party dependencies.

DeltaZulu’s Corresponding Source includes DeltaZulu source code, build scripts, project files, configuration needed to build DeltaZulu, and DeltaZulu modifications. It does not include unmodified third-party Microsoft Kusto Query Language components, which remain separately licensed.

## Distribution requirements

When distributing DeltaZulu in source, binary, container, installer, or package form, distributors must preserve applicable third-party copyright notices, license texts, package notices, and attribution files for Microsoft-published Kusto Query Language components.

If a distributed artifact includes `Microsoft.Azure.Kusto.Language` or any related Microsoft package, the distributor must comply with the applicable Microsoft license terms for that package.

If a distributed artifact uses Apache-2.0 licensed Kusto Query Language source or libraries, the distributor must comply with Apache License 2.0 notice and attribution requirements.

## No Azure Data Explorer connector

This exception is limited to Kusto Query Language support. It does not authorize or describe an Azure Data Explorer connector.

DeltaZulu is not Azure Data Explorer. DeltaZulu does not include an Azure Data Explorer data connector under this exception. Any future connector to Azure Data Explorer, Microsoft Fabric, Azure Monitor, or another Microsoft-hosted Kusto service would require a separate licensing and architectural review.

## Preservation of this exception

You may extend this exception to modified versions of DeltaZulu, but you are not required to do so.

If you do not wish to apply this exception to your modified version, remove this file and remove the exception notice from the modified version. In that case, you are responsible for ensuring that your modified version complies with AGPL-3.0 and with all applicable third-party dependency licenses.
