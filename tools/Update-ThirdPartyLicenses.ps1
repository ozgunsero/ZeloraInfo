<#
.SYNOPSIS
  Regenerates third-party-licenses.json (the manifest shown on the "Third-Party Licenses" screen).

.DESCRIPTION
  Edit the $components table below whenever a PackageReference is added, removed or upgraded,
  then run:  pwsh ./tools/Update-ThirdPartyLicenses.ps1
  Full MIT / Apache-2.0 texts are embedded so the app can display them offline.
  A unit test (ThirdPartyManifestTests) fails if a referenced package is missing from the manifest.

  Copyright 2026 Şero Teknoloji — Licensed under the Apache License, Version 2.0
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$mit = @'
MIT License

Copyright (c) {0}

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
'@

# Apache-2.0 terms = our LICENSE file up to the end of the appendix boilerplate.
$licenseFile = Get-Content (Join-Path $root 'LICENSE') -Raw -Encoding utf8
$apache = ($licenseFile -split '------------------------------------------------------------------------')[0].Trim()

function Purpose($tr, $en, $de, $fr, $it, $es) { [ordered]@{ tr = $tr; en = $en; de = $de; fr = $fr; it = $it; es = $es } }

$components = @(
    [ordered]@{
        name = '.NET Runtime (Microsoft.NETCore.App)'; version = '10.0'; license = 'MIT'; scope = 'runtime'
        purpose = Purpose 'Uygulamanın çalıştığı temel çalışma zamanı ve sınıf kitaplıkları.' 'Base runtime and class libraries the application runs on.' 'Basis-Laufzeit und Klassenbibliotheken der Anwendung.' 'Environnement d''exécution et bibliothèques de classes de base.' 'Runtime e librerie di classi di base dell''applicazione.' 'Entorno de ejecución y bibliotecas de clases base.'
        projectUrl = 'https://github.com/dotnet/runtime'; licenseUrl = 'https://github.com/dotnet/runtime/blob/main/LICENSE.TXT'
        copyright = 'Copyright (c) .NET Foundation and Contributors'; text = ($mit -f '.NET Foundation and Contributors')
    },
    [ordered]@{
        name = 'Windows Presentation Foundation (WPF)'; version = '10.0'; license = 'MIT'; scope = 'runtime'
        purpose = Purpose 'Masaüstü kullanıcı arayüzü çatısı ve Fluent tema.' 'Desktop user-interface framework and Fluent theme.' 'Desktop-UI-Framework und Fluent-Design.' 'Framework d''interface de bureau et thème Fluent.' 'Framework per l''interfaccia desktop e tema Fluent.' 'Framework de interfaz de escritorio y tema Fluent.'
        projectUrl = 'https://github.com/dotnet/wpf'; licenseUrl = 'https://github.com/dotnet/wpf/blob/main/LICENSE.TXT'
        copyright = 'Copyright (c) .NET Foundation and Contributors'; text = ($mit -f '.NET Foundation and Contributors')
    },
    [ordered]@{
        name = 'Windows Forms (NotifyIcon)'; version = '10.0'; license = 'MIT'; scope = 'runtime'
        purpose = Purpose 'Sistem tepsisi simgesi ve sağ tık menüsü.' 'System tray icon and its context menu.' 'Infobereich-Symbol und Kontextmenü.' 'Icône de la zone de notification et menu contextuel.' 'Icona dell''area di notifica e menu contestuale.' 'Icono de la bandeja del sistema y menú contextual.'
        projectUrl = 'https://github.com/dotnet/winforms'; licenseUrl = 'https://github.com/dotnet/winforms/blob/main/LICENSE.TXT'
        copyright = 'Copyright (c) .NET Foundation and Contributors'; text = ($mit -f '.NET Foundation and Contributors')
    },
    [ordered]@{
        name = 'CommunityToolkit.Mvvm'; version = '8.4.0'; license = 'MIT'; scope = 'runtime'
        purpose = Purpose 'MVVM altyapısı: gözlemlenebilir nesneler ve komutlar (kaynak üreteçleri).' 'MVVM infrastructure: observable objects and commands (source generators).' 'MVVM-Infrastruktur: beobachtbare Objekte und Befehle (Quellgeneratoren).' 'Infrastructure MVVM : objets observables et commandes (générateurs de source).' 'Infrastruttura MVVM: oggetti osservabili e comandi (generatori di sorgente).' 'Infraestructura MVVM: objetos observables y comandos (generadores de código).'
        projectUrl = 'https://github.com/CommunityToolkit/dotnet'; licenseUrl = 'https://github.com/CommunityToolkit/dotnet/blob/main/License.md'
        copyright = 'Copyright (c) .NET Foundation and Contributors'; text = ($mit -f '.NET Foundation and Contributors')
    },
    [ordered]@{
        name = 'System.Management'; version = '10.0.0'; license = 'MIT'; scope = 'runtime'
        purpose = Purpose 'WMI/CIM sorguları: diskler, işlemci, anakart, bellek modülleri, monitörler.' 'WMI/CIM queries: disks, processor, mainboard, memory modules, monitors.' 'WMI/CIM-Abfragen: Laufwerke, Prozessor, Mainboard, Speichermodule, Monitore.' 'Requêtes WMI/CIM : disques, processeur, carte mère, modules mémoire, écrans.' 'Query WMI/CIM: dischi, processore, scheda madre, moduli di memoria, monitor.' 'Consultas WMI/CIM: discos, procesador, placa base, módulos de memoria, monitores.'
        projectUrl = 'https://github.com/dotnet/runtime'; licenseUrl = 'https://github.com/dotnet/runtime/blob/main/LICENSE.TXT'
        copyright = 'Copyright (c) .NET Foundation and Contributors'; text = ($mit -f '.NET Foundation and Contributors')
    },
    [ordered]@{
        name = 'xunit'; version = '2.9.3'; license = 'Apache-2.0'; scope = 'development'
        purpose = Purpose 'Birim test çatısı (uygulamayla dağıtılmaz).' 'Unit-test framework (not shipped with the app).' 'Unit-Test-Framework (nicht in der App enthalten).' 'Framework de tests unitaires (non distribué).' 'Framework per test unitari (non distribuito).' 'Framework de pruebas unitarias (no se distribuye).'
        projectUrl = 'https://github.com/xunit/xunit'; licenseUrl = 'https://github.com/xunit/xunit/blob/main/LICENSE'
        copyright = 'Copyright (c) .NET Foundation and Contributors'; text = $apache
    },
    [ordered]@{
        name = 'xunit.runner.visualstudio'; version = '3.1.4'; license = 'Apache-2.0'; scope = 'development'
        purpose = Purpose 'dotnet test için test çalıştırıcı (uygulamayla dağıtılmaz).' 'Test runner for dotnet test (not shipped with the app).' 'Test-Runner für dotnet test (nicht in der App enthalten).' 'Exécuteur de tests pour dotnet test (non distribué).' 'Esecutore dei test per dotnet test (non distribuito).' 'Ejecutor de pruebas para dotnet test (no se distribuye).'
        projectUrl = 'https://github.com/xunit/visualstudio.xunit'; licenseUrl = 'https://github.com/xunit/visualstudio.xunit/blob/main/License.txt'
        copyright = 'Copyright (c) .NET Foundation and Contributors'; text = $apache
    },
    [ordered]@{
        name = 'Microsoft.NET.Test.Sdk'; version = '17.14.1'; license = 'MIT'; scope = 'development'
        purpose = Purpose 'Test barındırma altyapısı (uygulamayla dağıtılmaz).' 'Test hosting infrastructure (not shipped with the app).' 'Test-Hosting-Infrastruktur (nicht in der App enthalten).' 'Infrastructure d''hébergement des tests (non distribuée).' 'Infrastruttura di hosting dei test (non distribuita).' 'Infraestructura de alojamiento de pruebas (no se distribuye).'
        projectUrl = 'https://github.com/microsoft/vstest'; licenseUrl = 'https://github.com/microsoft/vstest/blob/main/LICENSE'
        copyright = 'Copyright (c) Microsoft Corporation'; text = ($mit -f 'Microsoft Corporation')
    },
    [ordered]@{
        name = 'coverlet.collector'; version = '6.0.4'; license = 'MIT'; scope = 'development'
        purpose = Purpose 'Kod kapsamı ölçümü (uygulamayla dağıtılmaz).' 'Code-coverage collection (not shipped with the app).' 'Codeabdeckungsmessung (nicht in der App enthalten).' 'Mesure de la couverture de code (non distribuée).' 'Misurazione della copertura del codice (non distribuita).' 'Medición de cobertura de código (no se distribuye).'
        projectUrl = 'https://github.com/coverlet-coverage/coverlet'; licenseUrl = 'https://github.com/coverlet-coverage/coverlet/blob/master/LICENSE'
        copyright = 'Copyright (c) 2018 Toni Solarin-Sodara'; text = ($mit -f '2018 Toni Solarin-Sodara')
    }
)

$manifest = [ordered]@{
    schemaVersion = 1
    updated = (Get-Date -Format 'yyyy-MM-dd')
    components = @($components | ForEach-Object {
        [ordered]@{
            name = $_.name; version = $_.version; license = $_.license; scope = $_.scope
            purpose = $_.purpose; projectUrl = $_.projectUrl; licenseUrl = $_.licenseUrl
            copyright = $_.copyright; licenseText = $_.text
        }
    })
}

$json = $manifest | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText((Join-Path $root 'third-party-licenses.json'), $json, (New-Object System.Text.UTF8Encoding $false))
Write-Output "third-party-licenses.json updated ($($components.Count) components)."
