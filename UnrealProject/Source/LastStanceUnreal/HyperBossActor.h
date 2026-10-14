#pragma once
#include "CoreMinimal.h"
#include "GameFramework/Actor.h"
#include "HyperBossActor.generated.h"
class UStaticMeshComponent; class UPointLightComponent; class AHyperFighterPawn;
UCLASS()
class LASTSTANCEUNREAL_API AHyperBossActor : public AActor
{
    GENERATED_BODY()
public:
    AHyperBossActor();
    void PowerPunch(AHyperFighterPawn* Target);
    void PulseAura();
private:
    UPROPERTY() UPointLightComponent* Aura;
    UPROPERTY() TArray<UStaticMeshComponent*> BodyPieces;
};
