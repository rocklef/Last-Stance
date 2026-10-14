#pragma once

#include "CoreMinimal.h"
#include "GameFramework/GameModeBase.h"
#include "HyperArenaGameMode.generated.h"

UCLASS()
class LASTSTANCEUNREAL_API AHyperArenaGameMode : public AGameModeBase
{
    GENERATED_BODY()
public:
    AHyperArenaGameMode();
    virtual void BeginPlay() override;
    virtual void Tick(float DeltaSeconds) override;
    void SelectMode(bool bStory);
    void TriggerBossPower();
    void ManualStrike();
private:
    void BuildHyperArena();
    void SpawnCombatants();
    void ShowLine(const FString& Line, const FColor& Colour = FColor::White, float Seconds = 3.f) const;
    UPROPERTY() class AHyperFighterPawn* Hero = nullptr;
    UPROPERTY() class AHyperBossActor* Boss = nullptr;
    UPROPERTY() class ARuinBreakWall* BreakWall = nullptr;
    bool bStoryMode = false;
    bool bBossPowerUsed = false;
    bool bWallBroken = false;
    float StoryElapsed = 0.f;
};
