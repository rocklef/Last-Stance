#include "HyperBossActor.h"
#include "HyperFighterPawn.h"
#include "Components/SceneComponent.h"
#include "Components/StaticMeshComponent.h"
#include "Components/PointLightComponent.h"
#include "Engine/StaticMesh.h"
AHyperBossActor::AHyperBossActor()
{
    USceneComponent* Root = CreateDefaultSubobject<USceneComponent>(TEXT("Root")); RootComponent = Root; UStaticMesh* Cube = LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Cube.Cube"));
    auto Piece = [this, Cube](const TCHAR* Name, FVector L, FVector S) { UStaticMeshComponent* Mesh = CreateDefaultSubobject<UStaticMeshComponent>(Name); Mesh->SetupAttachment(RootComponent); Mesh->SetStaticMesh(Cube); Mesh->SetRelativeLocation(L); Mesh->SetRelativeScale3D(S); BodyPieces.Add(Mesh); };
    Piece(TEXT("Torso"), FVector(0, 0, 150), FVector(.65f, .42f, .85f)); Piece(TEXT("Head"), FVector(0, 0, 265), FVector(.40f)); Piece(TEXT("PunchArm"), FVector(-80, -70, 155), FVector(.25f, .25f, .95f)); Piece(TEXT("GuardArm"), FVector(60, 70, 155), FVector(.25f, .25f, .85f)); Piece(TEXT("LegL"), FVector(0, -38, 55), FVector(.28f, .28f, .8f)); Piece(TEXT("LegR"), FVector(0, 38, 55), FVector(.28f, .28f, .8f));
    Aura = CreateDefaultSubobject<UPointLightComponent>(TEXT("RedAura")); Aura->SetupAttachment(RootComponent); Aura->SetLightColor(FLinearColor(1.f, .035f, .008f)); Aura->SetIntensity(180000.f); Aura->SetAttenuationRadius(2600.f);
}
void AHyperBossActor::PowerPunch(AHyperFighterPawn* Target) { PulseAura(); if (Target) Target->StartPowerLaunch(FVector(-1, 0, .12f), 3600.f); }
void AHyperBossActor::PulseAura() { Aura->SetIntensity(350000.f); }
