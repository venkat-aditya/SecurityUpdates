Param(
    [Parameter(Mandatory = $true)]
    $SerenityEnvironment,
    [Parameter(Mandatory = $true)]
    $AzureSubscription
)

Class SerenityEnvironments : System.Management.Automation.IValidateSetValuesGenerator {
    [String[]] GetValidValues() {
        return Get-SerenityEnvironmentNames
    }
}

$sourceDirectory = Join-Path -Resolve $PSScriptRoot '../../'
$infraPipeline = 'pipelines/cd/infra.yaml'
$containerRegistryPageSize = 100
$containerRegistryTagsUrl = 'https://registry.hub.docker.com/v2/repositories/{0}/{1}/tags/?page_size={2}'

Function Get-SerenityServiceNames {
    $services = Get-ChildItem -Path (Join-Path $sourceDirectory 'src') -Directory -Exclude services | Select-Object -ExpandProperty Name
    Get-ChildItem -Path (Join-Path $sourceDirectory 'src/services') -Directory |
        Select-Object -ExpandProperty Name |
        ForEach-Object {
            $services += $_
        }

    return $services
}

Class SerenityServiceNames : System.Management.Automation.IValidateSetValuesGenerator {
    [String[]] GetValidValues() {
        return Get-SerenityServiceNames
    }
}

Class SerenityService {
    $Name
    $SemanticVersion
    $GitSha
    $ImageTag
    $ImageName
    $ImageSha
    $PipelineRunId
    $CommitDate
}


Function Get-DockerHubTags {
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory=$true)]
        $Container,
        [Parameter(Mandatory=$false)]
        $Repository = 'library'
    )

    $result = Invoke-RestMethod ([System.String]::Format($containerRegistryTagsUrl, $Repository, $Container, $containerRegistryPageSize))
    if ($null -eq $result) {
        return
    }

    $results = @()
    $results += $result.results
    while ($null -ne $result.next -and '' -ne $result.next) {
        $result = Invoke-RestMethod $result.next
        if ($null -eq $result) {
            break
        }

        $results += $result.results
    }

    return $results
}

Function Get-SerenityService {
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [ValidateSet([SerenityServiceNames])]
        [Alias('ServiceName', 'Service')]
        $Name
    )

    Begin {}

    Process {
        $serenityServices = @()
        $pods = kubectl get pods --selector app.kubernetes.io/name=$Name --output json | ConvertFrom-Json -Depth 100 | Select-Object -ExpandProperty items
        foreach ($pod in $pods) {
            foreach ($container in $pods.spec.containers) {
                if ($container.name -ne $Name) {
                    continue
                }

                $serenityService = New-Object -TypeName SerenityService
                $serenityService.Name = $Name
                $imageNameAndTag = ($container.image -split '/')[-1]
                $serenityService.ImageTag = ($imageNameAndTag -split ':')[-1]
                $serenityService.ImageName = ($imageNameAndTag -split ':')[0]
                $imageId = $pod.status.containerStatuses | Where-Object { $_.name -eq $Name } | Select-Object -ExpandProperty imageID
                $serenityService.ImageSha = ($imageId -split '@')[-1]
                $serenityService.PipelineRunId = Get-DockerHubTags -Container $name -Repository azureiot3m |
                    Where-Object { $_.images[0].digest -eq $serenityService.ImageSha } |
                    Where-Object { $_.name -match '^\d{5,6}$' } |
                    Select-Object -ExpandProperty name
                $pipelineRun = az pipelines runs show --id $serenityService.PipelineRunId | ConvertFrom-Json -Depth 100
                Write-Host $pipelineRun
                $serenityService.GitSha = $pipelineRun.sourceVersion
                $serenityService.CommitDate = git show -s --format=%ci $serenityService.GitSha
                $serenityServices += $serenityService
            }
        }

        return $serenityServices
    }

    End {}
}

Function Get-SerenityServices {
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [ValidateSet([SerenityEnvironments])]
        [Alias('EnvironmentName')]
        $Environment
    )

    Begin {}

    Process {
        Set-K8sContext -Environment $Environment
        Get-SerenityServiceNames | Sort-Object | Get-SerenityService
    }

    End {}
}

Class SerenityEnvironment {
    $Name
    $DisplayName
    $AzureSubscriptionName
    $AzureSubscriptionId
    $AzureDevOpsEnvironmentName
    $ApplicationCode
    $ApplicationShortCode
    $EnvironmentCategory

    [string] GetAzureAksName() {
        return "$($this.ApplicationCode)-aks-$($this.EnvironmentCategory)"
    }

    [string] GetAzureResourceGroupName() {
        return "rg-iot-$($this.ApplicationShortCode)-$($this.EnvironmentCategory)"
    }
}

Function Get-SerenityEnvironments {
    $infraPipeline = ConvertFrom-Yaml (Get-Content -Raw (Join-Path $sourceDirectory $infraPipeline))
    $serenityEnvironments = @()
    foreach ($stageItem in $infraPipeline.stages) {
        $environment = New-Object -TypeName SerenityEnvironment
        $environment.Name = $stageItem.stage
        $environment.DisplayName = $stageItem.displayName
        foreach ($parameterItem in $stageItem.jobs |
            Where-Object { $_.template -ilike '*jobs-deploy-infra*' } |
            Select-Object -ExpandProperty parameters) {
                $environment.AzureSubscriptionName = $parameterItem.subscriptionName
                $environment.AzureSubscriptionId = $parameterItem.subscriptionId
                $environment.AzureDevOpsEnvironmentName = $parameterItem.environmentName
                $environment.ApplicationCode = $parameterItem.applicationCode
                $environment.ApplicationShortCode = $parameterItem.applicationShortCode
                $environment.EnvironmentCategory = $parameterItem.environmentCategory
            }

        $serenityEnvironments += $environment
    }

    return $serenityEnvironments
}

Function Get-SerenityEnvironment {
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [ValidateSet([SerenityEnvironments])]
        [Alias('EnvironmentName', 'Environment')]
        $Name
    )

    return Get-SerenityEnvironments | Where-Object { $_.Name -eq $Name }
}

Function Get-SerenityEnvironmentNames {
    return Get-SerenityEnvironments | Select-Object -ExpandProperty Name
}

Function Get-AzureAksCredentials {
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [ValidateSet([SerenityEnvironments])]
        [Alias('EnvironmentName')]
        $Environment
    )

    $serenityEnvironment = Get-SerenityEnvironment -Name $Environment
    az aks get-credentials --name $serenityEnvironment.GetAzureAksName() --resource-group $serenityEnvironment.GetAzureResourceGroupName()
}

Function Set-K8sContext {
    [CmdletBinding()]
    Param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [ValidateSet([SerenityEnvironments])]
        [Alias('EnvironmentName')]
        $Environment
    )

    Get-AzureAksCredentials -Environment $Environment
    $serenityEnvironment = Get-SerenityEnvironment -Name $Environment
    kubectl config use-context $serenityEnvironment.GetAzureAksName()
    kubectl config current-context
}

az account set --subscription $AzureSubscription
Install-Module -Force -AcceptLicense powershell-yaml
Write-Host Hello, world!
$Null = New-Item (Join-Path $sourceDirectory reports) -ItemType Directory
Set-K8sContext -Environment $SerenityEnvironment
Get-SerenityServiceNames | Sort-Object | Get-SerenityService #| ConvertTo-Html | Set-Content -Path (Join-Path $sourceDirectory reports "$SerenityEnvironment.html")