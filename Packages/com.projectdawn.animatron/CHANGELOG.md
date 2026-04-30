# Changelog
All notable changes to this package will be documented in this file. The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)

## [1.5.0] - 2025-10-22
- Changed skin matrix buffer gpu structural code. Greatly reducing vram usage and allow even more animatrons
- Fixed crash on having few thousands of animatrons, now should able to support up to 4 million joints accross all the animatrons
- Added animatron settings
- Added animatron setting to control initial capacity of total joints, thus allowing user to reduce reallocations
- Added in animatron settings metrics to track vram usage and total joints currently in use

## [1.4.2] - 2025-10-17
- Removed unused namespace in Animatron Authoring

## [1.4.1] - 2025-10-15
- Added new option ApplyFootIK for humanoid animation baking
- Added hybrid integration with agents navigation
- Fixed warnings in unity 6 for used deprecated GetScriptingDefineSymbolsForGroup
- Fixed AffineTransform working that was caused by blendspace regression

## [1.4.0] - 2025-10-07
- Added BlendSpace2D
- Added BlendSpace1D
- Added new option in animatron to store previous pose instead of calculating it from animation

## [1.3.1] - 2025-09-08
- Fixed on entity instantiate having first frame artifacts

## [1.3.0] - 2025-08-28
- Changed UI should be much easier to navigate
- Added events for each animation

## [1.2.2] - 2025-08-12
- Fixed animatron transform reseting then new animatron would be created
- Added Render Mesh Array support for selecting multiple animatrons
- Added Render Mesh Array to render in UI material previews
- Adder Render Mesh Array drag & drop material into existing material for switching similar to Mesh Renderer
- Fixed Render Mesh Array not to reset render bounds, if material was changed

## [1.2.1] - 2025-08-07
- Added inertialization unit and performance tests
- Fixed inertialization then anything played after it
- Optimized inertialization with SIMD that yield ~20% cpu cost reduction

## [1.2.0] - 2025-08-06
- Fixed inertialization popping effect
- Fixed inertialization then overlapping transsition happens
- Small refactor of CrossFader structure
- Small refactor of Inertializer structure

## [1.1.0] - 2025-08-03
- Added animation clip humanoid support
- Added to animator GetJointWorldTransform, TryFindJointIndex and FindJointIndex to make attachments easier
- Added prefab generation to inherit scales from prefab
- Added new scene ArcherHumanoid in TerracottaArmy
- Removed entities dependency from package as ecs graphics has its own
- Fixed WarriorVisualizeBlend and WarriorShieldAttachment to work with other transform types

## [1.0.0] - 2025-07-31
- Package released