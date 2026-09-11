# Prompt para OpenCode — Doom Clone (1 fase) em Unity

Cole o prompt abaixo no OpenCode com o Unity-MCP configurado e o projeto Unity aberto.

---

## PROMPT

Quero que você crie um clone do Doom (FPS old-school, ritmo rápido, corredores + arenas) dentro deste projeto Unity, usando as ferramentas do Unity MCP para criar GameObjects, componentes, cenas, prefabs e scripts diretamente no Editor. É uma única fase (single level), sem sistema de progressão entre fases.

### 1. Estrutura da cena
- Crie uma cena chamada `DoomClone_Level01`.
- Construa um layout simples com pelo menos: 1 arena central grande (para as hordas), 2-3 corredores conectando a áreas secundárias, e pontos de spawn de inimigos distribuídos (marque como GameObjects vazios com tag `EnemySpawnPoint`).
- Adicione um ponto de spawn do jogador (`PlayerSpawnPoint`).
- Geometria pode ser feita com primitivos (cubos escalados para paredes/chão/teto) — não precisa de asset externo.
- Iluminação básica estilo corredor sombrio (point lights pontuais, sem luz ambiente forte).

### 2. Player (FPS Controller)
- CharacterController + câmera em primeira pessoa.
- Movimento WASD + mouse look, sprint (Shift), pulo simples.
- Vida do jogador: 100 HP. Ao chegar a 0 → tela de "Game Over" e reinício da fase.
- HUD simples (Canvas): vida atual, arma equipada, munição da arma atual, contador de hordas.

### 3. Armas (4 armas, todas disponíveis desde o início, troca via teclas 1-4)
Cada arma é um script próprio derivado de uma classe base `Weapon`, com: dano por hit, cadência de tiro, alcance/hitscan ou projétil, munição (infinita ou limitada — definir abaixo), efeito visual simples de disparo (pode ser um flash de luz + partícula simples).

1. **Motosserra (Chainsaw)**
   - Arma corpo a corpo, curto alcance.
   - Dano alto por segundo enquanto o botão é segurado contra um inimigo.
   - Sem munição (uso ilimitado).
   - Som/animação de "ligada" contínua enquanto ataca (pode ser um efeito visual simples de vibração da câmera).

2. **Pistola**
   - Hitscan, dano baixo-médio, cadência de tiro média, sem necessidade de recarga (ou recarga rápida automática).
   - Munição inicial generosa, é a arma "padrão" de fallback.

3. **Escopeta (Shotgun)**
   - Hitscan múltiplo (vários raios em cone curto, tipo 6-8 pellets), dano alto em curta distância, cadência lenta (delay entre tiros perceptível), recarga por cartucho ou em bloco.
   - Munição limitada, mais escassa que a pistola.

4. **Fuzil de Assalto (Assault Rifle)**
   - Hitscan, cadência alta (automático, segurar o botão), dano médio por tiro, alcance longo.
   - Munição limitada, gasta rápido por causa da cadência.

Todas as armas com efeito de impacto (partícula simples) ao acertar inimigo ou parede.

### 4. Inimigos (3 tipos distintos)
Crie uma classe base `Enemy` (vida, dano de ataque, velocidade, IA de perseguição via NavMesh) e derive 3 tipos com comportamentos claramente diferentes:

1. **Zumbi Corredor (Melee rápido)**
   - Vida baixa, velocidade alta, ataque corpo a corpo de dano baixo-médio mas com cadência rápida.
   - Comportamento: corre direto na linha reta até o player assim que detecta.

2. **Soldado à Distância (Ranged médio)**
   - Vida média, velocidade média, ataque à distância (hitscan ou projétil lento) de dano médio.
   - Comportamento: mantém distância do player (tenta ficar a uma faixa de range ideal), atira em intervalos, pode se reposicionar (strafing simples) se o player chegar perto demais.

3. **Bruto Tanque (Melee/curto alcance, pesado)**
   - Vida alta, velocidade baixa, dano de ataque alto mas cadência lenta, hitbox maior (escala do modelo maior).
   - Comportamento: avança devagar, ataque tem um "telegraph" (pequena pausa/animação de preparação de 0.5s antes de bater) para dar chance de desviar.

Use cápsulas/primitivos com cores diferentes por tipo (ex: verde para o corredor, laranja para o soldado, vermelho escuro para o bruto) já que não há assets de modelo — foco na mecânica.

Todos os inimigos devem morrer ao HP zerar (destruir o GameObject + tocar efeito simples de morte) e o player recebe dano ao ser atingido por qualquer um deles.

### 5. Sistema de Hordas (Wave Manager)
Crie um `WaveManager` (singleton na cena) com esta lógica exata:
- O jogo começa a spawnar a **Horda 1** assim que a cena carrega (após um pequeno delay inicial de 2s).
- Cada horda spawna um número de inimigos nos `EnemySpawnPoint`s da cena, misturando os 3 tipos (a proporção e quantidade devem escalar a cada horda — ex: horda 1 com poucos inimigos e mais fracos em proporção, hordas seguintes aumentando quantidade e proporção de inimigos mais fortes).
- O `WaveManager` monitora a contagem de inimigos vivos da horda atual.
- **Quando o último inimigo da horda atual morre → inicia um cooldown de exatamente 5 segundos** (exiba contagem regressiva no HUD, tipo "Próxima horda em: 5...4...3...2...1").
- Ao fim do cooldown, spawna a próxima horda automaticamente.
- **Isso se repete indefinidamente, sem fim** — não há última horda nem vitória por sobrevivência; hordas continuam surgindo para sempre, aumentando a dificuldade progressivamente (mais inimigos e/ou mais bruto tanques a cada N hordas), até o player morrer.
- Ao jogador morrer, o loop de hordas para e mostra a tela de Game Over com o número de hordas sobrevividas.

### 6. Ordem de implementação sugerida (siga essa sequência)
1. Crie a cena e a geometria básica do nível.
2. Implemente o Player Controller + câmera + HUD básico.
3. Implemente a classe base `Weapon` e as 4 armas, com troca de arma funcionando.
4. Implemente a classe base `Enemy` e os 3 tipos, com NavMesh configurado no nível.
5. Implemente o `WaveManager` com spawn, detecção de horda limpa, cooldown de 5s e spawn infinito escalando dificuldade.
6. Testes: entre em Play Mode, valide que as 4 armas funcionam, que os 3 inimigos se comportam diferente, e que o cooldown de 5s entre hordas está correto.
7. Ajustes finais de balanceamento (dano, vida, cadência) para o jogo ser jogável, não frustrante nem trivial demais.

Vá em etapas, compile e teste cada parte antes de seguir para a próxima (use as ferramentas de compilação/console do Unity MCP para pegar erros cedo). Me avise se precisar de alguma decisão de design que eu não tenha especificado.
