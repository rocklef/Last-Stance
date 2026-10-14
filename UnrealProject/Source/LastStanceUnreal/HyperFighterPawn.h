#pragma once
#include "CoreMinimal.h"
#include "GameFramework/Pawn.h"
#include "HyperFighterPawn.generated.h"
class UCapsuleComponent; class UCameraComponent; class USpringArmComponent; class UStaticMeshComponent;
UCLASS()
class LASTSTANCEUNREAL_API AHyperFighterPawn : public APawn
{
    GENERATED_BODY()
public:
    AHyperFighterPawn();
    virtual void Tick(float DeltaSeconds) override;
    virtual void SetupPlayerInputComponent(UInputComponent* PlayerInputComponent) override;
    void StartPowerLaunch(const FVector& Direction, float Strength);
    void EnterRagdoll(const FVector& Impulse);
    void ResetFighter();
    bool IsLaunching() const { return bLaunching; }
    bool IsRagdolled() const { return bRagdolled; }
private:
    void MoveForward(float Value); void MoveRight(float Value); void LightStrike(); void HeavyStrike(); void ManualMode(); void StoryMode(); void BossPower();
    UStaticMeshComponent* MakePiece(const FName Name, const FVector& Location, const FVector& Scale);
    UPROPERTY(VisibleAnywhere) UCapsuleComponent* Capsule;
    UPROPERTY(VisibleAnywhere) USpringArmComponent* CameraBoom;
    UPROPERTY(VisibleAnywhere) UCameraComponent* Camera;
    UPROPERTY() TArray<UStaticMeshComponent*> PhysicsPieces;
    FVector FlightVelocity = FVector::ZeroVector;
    bool bLaunching = false; bool bRagdolled = false;
};
