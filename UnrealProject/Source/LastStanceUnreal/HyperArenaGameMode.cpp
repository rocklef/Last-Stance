#include "HyperArenaGameMode.h"
#include "HyperBossActor.h"
#include "HyperFighterPawn.h"
#include "RuinBreakWall.h"
#include "Engine/Engine.h"
#include "Engine/StaticMeshActor.h"
#include "Engine/World.h"
#include "Engine/DirectionalLight.h"
#include "Engine/SkyLight.h"
#include "GameFramework/PlayerController.h"
#include "Components/StaticMeshComponent.h"
#include "Components/DirectionalLightComponent.h"
#include "Components/SkyLightComponent.h"
#include "Components/PointLightComponent.h"

AHyperArenaGameMode::AHyperArenaGameMode() { DefaultPawnClass = nullptr; PrimaryActorTick.bCanEverTick = true; }
void AHyperArenaGameMode::BeginPlay() { Super::BeginPlay(); UE_LOG(LogTemp, Display, TEXT("LastStance: Arena setup started")); BuildHyperArena(); SpawnCombatants(); ShowLine(TEXT("LAST STANCE // 1: MANUAL   2: STORY   J: STRIKE   K: HEAVY   R: BOSS POWER"), FColor(240, 198, 90), 8.f); }
void AHyperArenaGameMode::Tick(float DeltaSeconds)
{
    Super::Tick(DeltaSeconds); if (!bStoryMode || !Hero || Hero->IsRagdolled()) return; StoryElapsed += DeltaSeconds;
    if (StoryElapsed > 2.f && StoryElapsed < 2.1f) ShowLine(TEXT("VARKOS: You came to my temple alone."), FColor(255, 85, 55));
    if (StoryElapsed > 5.f && StoryElapsed < 5.1f) ShowLine(TEXT("HERO: I came to end this."), FColor(105, 190, 255));
    if (StoryElapsed > 8.f && !bBossPowerUsed) TriggerBossPower();
    if (Hero->IsLaunching() && !bWallBroken && Hero->GetActorLocation().X < -2350.f) { bWallBroken = true; BreakWall->Shatter(FVector(-1, 0, .22f) * 240000.f); Hero->EnterRagdoll(FVector(-1, 0, .28f) * 220000.f); ShowLine(TEXT("RUINBREAKER IMPACT"), FColor(255, 190, 70), 2.f); }
}
void AHyperArenaGameMode::SelectMode(bool bStory) { bStoryMode = bStory; bBossPowerUsed = false; bWallBroken = false; StoryElapsed = 0.f; if (Hero) Hero->ResetFighter(); ShowLine(bStory ? TEXT("STORY MODE // THE TEMPLE OF VARKOS") : TEXT("MANUAL MODE // COMBAT SANDBOX"), FColor(245, 215, 110), 4.f); }
void AHyperArenaGameMode::TriggerBossPower() { if (!Hero || !Boss || Hero->IsLaunching() || Hero->IsRagdolled()) return; bBossPowerUsed = true; Boss->PowerPunch(Hero); ShowLine(TEXT("VARKOS UNLEASHES: TITAN BREAKER"), FColor(255, 60, 30), 3.f); }
void AHyperArenaGameMode::ManualStrike() { ShowLine(TEXT("COMBO WINDOW // J LIGHT • K HEAVY • R TEST TITAN BREAKER"), FColor(110, 205, 255), 1.3f); if (Boss) Boss->PulseAura(); }
void AHyperArenaGameMode::BuildHyperArena()
{
    UWorld* World = GetWorld(); UStaticMesh* Cube = LoadObject<UStaticMesh>(nullptr, TEXT("/Engine/BasicShapes/Cube.Cube")); if (!World || !Cube) return;
    auto Block = [World, Cube](const FVector& At, const FVector& Scale) { AStaticMeshActor* Piece = World->SpawnActor<AStaticMeshActor>(At, FRotator::ZeroRotator); Piece->GetStaticMeshComponent()->SetStaticMesh(Cube); Piece->SetActorScale3D(Scale); Piece->GetStaticMeshComponent()->SetMobility(EComponentMobility::Movable); };
    Block(FVector(0, 0, -115), FVector(38, 38, .3f));
    for (int32 i = -5; i <= 5; ++i) { Block(FVector(i * 1200.f, -4200.f, 350.f), FVector(.36f, .36f, 7.f)); Block(FVector(i * 1200.f, 4200.f, 350.f), FVector(.36f, .36f, 7.f)); }
    Block(FVector(-2800, 0, 1050), FVector(.5f, 15.f, .25f));
    BreakWall = World->SpawnActor<ARuinBreakWall>(FVector(-2700, 0, 0), FRotator::ZeroRotator);
    ADirectionalLight* Sun = World->SpawnActor<ADirectionalLight>(FVector(0, 0, 2500), FRotator(-48, -35, 0));
    Sun->GetLightComponent()->SetIntensity(12.f); Sun->GetLightComponent()->SetLightColor(FLinearColor(1.f, .34f, .13f));
    ASkyLight* Sky = World->SpawnActor<ASkyLight>(FVector(0, 0, 1800), FRotator::ZeroRotator);
    Sky->GetLightComponent()->SetIntensity(2.2f); Sky->GetLightComponent()->SetLightColor(FLinearColor(.08f, .18f, .55f));
    for (int32 Side : {-1, 1}) { AActor* Fill = World->SpawnActor<AActor>(FVector(-1100, Side * 1600.f, 600), FRotator::ZeroRotator); UPointLightComponent* Light = NewObject<UPointLightComponent>(Fill); Light->RegisterComponent(); Fill->SetRootComponent(Light); Light->SetIntensity(180000.f); Light->SetAttenuationRadius(3400.f); Light->SetLightColor(Side < 0 ? FLinearColor(1.f, .05f, .01f) : FLinearColor(.04f, .23f, 1.f)); }
}
void AHyperArenaGameMode::SpawnCombatants() { UWorld* World = GetWorld(); if (!World) return; Hero = World->SpawnActor<AHyperFighterPawn>(FVector(0, 0, 100), FRotator(0, 180, 0)); Boss = World->SpawnActor<AHyperBossActor>(FVector(-850, 0, 120), FRotator(0, 0, 0)); if (APlayerController* PC = World->GetFirstPlayerController()) { PC->Possess(Hero); PC->SetViewTarget(Hero); } UE_LOG(LogTemp, Display, TEXT("LastStance: Hero=%s Boss=%s"), *GetNameSafe(Hero), *GetNameSafe(Boss)); }
void AHyperArenaGameMode::ShowLine(const FString& Line, const FColor& Colour, float Seconds) const { if (GEngine) GEngine->AddOnScreenDebugMessage(-1, Seconds, Colour, Line); }
