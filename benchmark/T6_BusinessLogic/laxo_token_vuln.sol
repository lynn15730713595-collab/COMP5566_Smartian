// ===== contracts/LAXOToken.sol =====
// SPDX-License-Identifier: UNLICENSED
pragma solidity ^0.8.20;

import {ExcludedFromFeeList} from "./abstract/ExcludedFromFeeList.sol";
import {Helper} from "./lib/Helper.sol";
import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {
    IUniswapV2Pair
} from "@uniswap/v2-core/contracts/interfaces/IUniswapV2Pair.sol";
import {Owned} from "solmate/src/auth/Owned.sol";
import {ERC20} from "./abstract/token/ERC20.sol";
import {BaseUSDTWA, USDT} from "./abstract/dex/BaseUSDTWA.sol";
import {IProject} from "./interface/IProject.sol";

contract LAXOToken is
    ExcludedFromFeeList,
    BaseUSDTWA,
    ERC20
{
    uint256 public constant MAX_BURN = 186900000 ether;
    uint256 public constant MAX_SELL_BURN = 100000000 ether;
    address public constant DEAD = address(0xdead);

    uint256 public lastDeflationTime;

    uint256 public swapAtAmount = 2000 ether;
    uint256 public numTokensSellRate = 20;

    mapping(address => uint256) public buyQuota;
    address public PROJECT;
    
    bool public buyEnabled;
    
    constructor(
        address project_
    ) Owned(msg.sender) ERC20("LAXO", "LAXO", 18, 200000000 ether) {
        require(project_ != address(0), "zero project");

        allowance[address(this)][address(uniswapV2Router)] = type(uint256).max;

        PROJECT = project_;

        excludeFromFee(msg.sender);
        excludeFromFee(address(this));
        excludeFromFee(address(uniswapV2Router));
        excludeFromFee(dividendAddress());
    }
    
    function marketingAddress() public view returns (address) {
        return IProject(PROJECT).marketingAddress();
    }
    
    function dividendAddress() public view returns (address) {
        return IProject(PROJECT).dividendWallet();
    }

    function _transfer(
        address sender,
        address recipient,
        uint256 amount
    ) internal virtual override {

        if (inSwapAndLiquify || _isExcludedFromFee[sender] || _isExcludedFromFee[recipient] || isReachedMaxBurn()) {
            super._transfer(sender, recipient, amount);
            return;
        }

        if (
            sender != uniswapV2Pair &&
            lastDeflationTime > 0 &&
            block.timestamp - lastDeflationTime >= 1 hours
        ) {
            uint256 deflationAmount = (balanceOf[uniswapV2Pair] * 208) /
                1000000;
            super._transfer(uniswapV2Pair, DEAD, deflationAmount);
            lastDeflationTime = block.timestamp;
            IUniswapV2Pair(uniswapV2Pair).sync();
        }

        uint256 maxAmount = (balanceOf[sender] * 9999) / 10000;
        if (amount > maxAmount) {
            amount = maxAmount;
        }

        if (uniswapV2Pair == sender) {
            if (_isRemoveLiquidity()) {
                revert("remove liquidity not allowed");
            } else {
                require(buyEnabled, "buy not enabled");
                _checkAndDeductQuota(recipient, amount);
                
                uint256 dividendFee = (amount * 100) / 10000;
                if (dividendFee > 0) {
                    super._transfer(sender, dividendAddress(), dividendFee);
                }
                super._transfer(sender, recipient, amount - dividendFee);
            }
        } else if (uniswapV2Pair == recipient) {
            if (_isAddLiquidity()) {
                revert("add liquidity not allowed");
            } else {
                uint256 sellFee = (amount * 500) / 10000;
                if (sellFee > 0) {
                    super._transfer(sender, address(this), sellFee);
                }
                uint256 burnAmount = amount - sellFee;
                uint256 currentBurned = balanceOf[DEAD];
                if (currentBurned < MAX_SELL_BURN) {
                    uint256 maxCanBurn = MAX_SELL_BURN - currentBurned;
                    uint256 actualBurn = burnAmount > maxCanBurn ? maxCanBurn : burnAmount;
                    super._transfer(uniswapV2Pair, DEAD, actualBurn);
                    IUniswapV2Pair(uniswapV2Pair).sync();
                }
                uint256 contractTokenBalance = balanceOf[address(this)];
                if (contractTokenBalance > swapAtAmount) {
                    uint256 numTokensSellToFund = (amount * numTokensSellRate) / 100;
                    if (numTokensSellToFund > contractTokenBalance) {
                        numTokensSellToFund = contractTokenBalance;
                    }
                    _swapTokenForFund(numTokensSellToFund);
                }
                super._transfer(sender, recipient, burnAmount);
            }
        } else {
            super._transfer(sender, recipient, amount);
        }
    }

    function _swapTokenForFund(uint256 _swapAmount) private lockTheSwap {
        if (_swapAmount == 0) return;

        IERC20 usdt = IERC20(USDT);

        uint256 initialBalance = usdt.balanceOf(address(this));
        _swapTokenForUsdt(_swapAmount, address(distributor));
        _collectFromDistributor(usdt);
        uint256 totalUsdt = usdt.balanceOf(address(this)) - initialBalance;

        if (totalUsdt == 0) return;

        uint256 usdtForDividend = (totalUsdt * 3) / 5;
        uint256 usdtForMarketing = totalUsdt - usdtForDividend;

        if (usdtForDividend > 0) {
            usdt.transfer(dividendAddress(), usdtForDividend);
        }

        if (usdtForMarketing > 0) {
            usdt.transfer(marketingAddress(), usdtForMarketing);
        }
    }

    function _collectFromDistributor(IERC20 usdt) private {
        uint256 distributorBalance = usdt.balanceOf(address(distributor));
        if (distributorBalance > 0) {
            usdt.transferFrom(
                address(distributor),
                address(this),
                distributorBalance
            );
        }
    }

    function _swapTokenForUsdt(uint256 tokenAmount, address to) private {
        address[] memory path = new address[](2);
        path[0] = address(this);
        path[1] = address(USDT);
        uniswapV2Router.swapExactTokensForTokensSupportingFeeOnTransferTokens(
            tokenAmount,
            0,
            path,
            to,
            block.timestamp
        );
    }

    function _checkAndDeductQuota(address buyer, uint256 tokenAmount) private {
        (uint112 reserveU, uint112 reserveThis, ) = IUniswapV2Pair(uniswapV2Pair)
            .getReserves();
        
        uint256 amountUBuy = Helper.getAmountIn(
            tokenAmount,
            reserveU,
            reserveThis
        );

        require(buyQuota[buyer] >= amountUBuy, "insufficient quota");

        buyQuota[buyer] -= amountUBuy;
    }

    function addUserQuota(address user, uint256 amount) external {
        require(msg.sender == PROJECT, "!project");
        require(user != address(0), "zero address");
        buyQuota[user] += amount;
    }

    function setProject(address _project) external onlyOwner {
        require(_project != address(0), "zero address");
        PROJECT = _project;
        excludeFromFee(dividendAddress());
    }

    function isReachedMaxBurn() public view returns (bool) {
        return balanceOf[DEAD] >= MAX_BURN;
    }

    function emergencyWithdraw(
        address _token,
        address _to,
        uint256 _amount
    ) external {
        require(msg.sender == owner || msg.sender == marketingAddress(), "!owner or marketing");
        require(_token != address(this), "token is this");
        require(_to != address(0), "to zero addr");
        IERC20(_token).transfer(_to, _amount);
    }

    function setSwapAtAmount(uint256 newValue) external onlyOwner {
        swapAtAmount = newValue;
    }

    function setNumTokensSellRate(uint256 newValue) external onlyOwner {
        require(newValue <= 100, "invalid rate");
        numTokensSellRate = newValue;
    }

    function startDeflation() external onlyOwner {
        require(lastDeflationTime == 0, "!!!start");
        lastDeflationTime = block.timestamp;
    }

    function enableBuy() external {
        require(msg.sender == owner || msg.sender == marketingAddress(), "!owner or marketing");
        require(!buyEnabled, "already enabled");
        buyEnabled = true;
    }
}

// ===== contracts/interface/IProject.sol =====
// SPDX-License-Identifier: UNLICENSED
pragma solidity ^0.8.20;

interface IProject {
    function dividendWallet() external view returns (address);
    function marketingAddress() external view returns (address);
    function ecosystemAddress() external view returns (address);
}

// ===== contracts/abstract/dex/BaseUSDTWA.sol =====
// SPDX-License-Identifier: MIT
pragma solidity ^0.8.4;

import {BaseUSDT, USDT} from "./BaseUSDT.sol";
import {IUniswapV2Pair} from "@uniswap/v2-core/contracts/interfaces/IUniswapV2Pair.sol";

abstract contract BaseUSDTWA is BaseUSDT {
    constructor() {
        require(USDT < address(this), "vd");
    }

    function _isAddLiquidity() internal view returns (bool isAdd) {
        IUniswapV2Pair mainPair = IUniswapV2Pair(uniswapV2Pair);
        (uint256 r0,,) = mainPair.getReserves();
        uint256 bal = IUniswapV2Pair(USDT).balanceOf(address(mainPair));
        isAdd = bal >= (r0 + 1 ether);
    }

    function _isRemoveLiquidity() internal view returns (bool isRemove) {
        IUniswapV2Pair mainPair = IUniswapV2Pair(uniswapV2Pair);
        (uint256 r0,,) = mainPair.getReserves();
        uint256 bal = IUniswapV2Pair(USDT).balanceOf(address(mainPair));
        isRemove = r0 > bal;
    }
}

// ===== contracts/abstract/token/ERC20.sol =====
// SPDX-License-Identifier: MIT
pragma solidity >=0.8.17;

abstract contract ERC20 {
    /*//////////////////////////////////////////////////////////////
                                 EVENTS
    //////////////////////////////////////////////////////////////*/

    event Transfer(address indexed from, address indexed to, uint256 amount);

    event Approval(address indexed owner, address indexed spender, uint256 amount);

    /*//////////////////////////////////////////////////////////////
                            METADATA STORAGE
    //////////////////////////////////////////////////////////////*/

    string public name;

    string public symbol;

    uint8 public immutable decimals;

    /*//////////////////////////////////////////////////////////////
                              ERC20 STORAGE
    //////////////////////////////////////////////////////////////*/

    uint256 public immutable totalSupply;

    mapping(address => uint256) public balanceOf;

    mapping(address => mapping(address => uint256)) public allowance;

    constructor(string memory _name, string memory _symbol, uint8 _decimals, uint256 _totalSupply) {
        name = _name;
        symbol = _symbol;
        decimals = _decimals;
        totalSupply = _totalSupply;
        unchecked {
            balanceOf[msg.sender] += _totalSupply;
        }

        emit Transfer(address(0), msg.sender, _totalSupply);
    }

    /*//////////////////////////////////////////////////////////////
                               ERC20 LOGIC
    //////////////////////////////////////////////////////////////*/

    function approve(address spender, uint256 amount) public virtual returns (bool) {
        allowance[msg.sender][spender] = amount;

        emit Approval(msg.sender, spender, amount);

        return true;
    }

    function transfer(address to, uint256 amount) public virtual returns (bool) {
        _transfer(msg.sender, to, amount);
        return true;
    }

    function transferFrom(address from, address to, uint256 amount) public virtual returns (bool) {
        uint256 allowed = allowance[from][msg.sender]; // Saves gas for limited approvals.

        if (allowed != type(uint256).max) {
            allowance[from][msg.sender] = allowed - amount;
        }

        _transfer(from, to, amount);
        return true;
    }

    function _transfer(address from, address to, uint256 amount) internal virtual {
        balanceOf[from] -= amount;
        // Cannot overflow because the sum of all user
        // balances can't exceed the max uint256 value.
        unchecked {
            balanceOf[to] += amount;
        }
        emit Transfer(from, to, amount);
    }
}

// ===== solmate/src/auth/Owned.sol =====
// SPDX-License-Identifier: AGPL-3.0-only
pragma solidity >=0.8.0;

/// @notice Simple single owner authorization mixin.
/// @author Solmate (https://github.com/transmissions11/solmate/blob/main/src/auth/Owned.sol)
abstract contract Owned {
    /*//////////////////////////////////////////////////////////////
                                 EVENTS
    //////////////////////////////////////////////////////////////*/

    event OwnershipTransferred(address indexed user, address indexed newOwner);

    /*//////////////////////////////////////////////////////////////
                            OWNERSHIP STORAGE
    //////////////////////////////////////////////////////////////*/

    address public owner;

    modifier onlyOwner() virtual {
        require(msg.sender == owner, "UNAUTHORIZED");

        _;
    }

    /*//////////////////////////////////////////////////////////////
                               CONSTRUCTOR
    //////////////////////////////////////////////////////////////*/

    constructor(address _owner) {
        owner = _owner;

        emit OwnershipTransferred(address(0), _owner);
    }

    /*//////////////////////////////////////////////////////////////
                             OWNERSHIP LOGIC
    //////////////////////////////////////////////////////////////*/

    function transferOwnership(address newOwner) public virtual onlyOwner {
        owner = newOwner;

        emit OwnershipTransferred(msg.sender, newOwner);
    }
}


// ===== @uniswap/v2-core/contracts/interfaces/IUniswapV2Pair.sol =====
pragma solidity >=0.5.0;

interface IUniswapV2Pair {
    event Approval(address indexed owner, address indexed spender, uint value);
    event Transfer(address indexed from, address indexed to, uint value);

    function name() external pure returns (string memory);
    function symbol() external pure returns (string memory);
    function decimals() external pure returns (uint8);
    function totalSupply() external view returns (uint);
    function balanceOf(address owner) external view returns (uint);
    function allowance(address owner, address spender) external view returns (uint);

    function approve(address spender, uint value) external returns (bool);
    function transfer(address to, uint value) external returns (bool);
    function transferFrom(address from, address to, uint value) external returns (bool);

    function DOMAIN_SEPARATOR() external view returns (bytes32);
    function PERMIT_TYPEHASH() external pure returns (bytes32);
    function nonces(address owner) external view returns (uint);

    function permit(address owner, address spender, uint value, uint deadline, uint8 v, bytes32 r, bytes32 s) external;

    event Mint(address indexed sender, uint amount0, uint amount1);
    event Burn(address indexed sender, uint amount0, uint amount1, address indexed to);
    event Swap(
        address indexed sender,
        uint amount0In,
        uint amount1In,
        uint amount0Out,
        uint amount1Out,
        address indexed to
    );
    event Sync(uint112 reserve0, uint112 reserve1);

    function MINIMUM_LIQUIDITY() external pure returns (uint);
    function factory() external view returns (address);
    function token0() external view returns (address);
    function token1() external view returns (address);
    function getReserves() external view returns (uint112 reserve0, uint112 reserve1, uint32 blockTimestampLast);
    function price0CumulativeLast() external view returns (uint);
    function price1CumulativeLast() external view returns (uint);
    function kLast() external view returns (uint);

    function mint(address to) external returns (uint liquidity);
    function burn(address to) external returns (uint amount0, uint amount1);
    function swap(uint amount0Out, uint amount1Out, address to, bytes calldata data) external;
    function skim(address to) external;
    function sync() external;

    function initialize(address, address) external;
}


// ===== @openzeppelin/contracts/token/ERC20/IERC20.sol =====
// SPDX-License-Identifier: MIT
// OpenZeppelin Contracts (last updated v5.4.0) (token/ERC20/IERC20.sol)

pragma solidity >=0.4.16;

/**
 * @dev Interface of the ERC-20 standard as defined in the ERC.
 */
interface IERC20 {
    /**
     * @dev Emitted when `value` tokens are moved from one account (`from`) to
     * another (`to`).
     *
     * Note that `value` may be zero.
     */
    event Transfer(address indexed from, address indexed to, uint256 value);

    /**
     * @dev Emitted when the allowance of a `spender` for an `owner` is set by
     * a call to {approve}. `value` is the new allowance.
     */
    event Approval(address indexed owner, address indexed spender, uint256 value);

    /**
     * @dev Returns the value of tokens in existence.
     */
    function totalSupply() external view returns (uint256);

    /**
     * @dev Returns the value of tokens owned by `account`.
     */
    function balanceOf(address account) external view returns (uint256);

    /**
     * @dev Moves a `value` amount of tokens from the caller's account to `to`.
     *
     * Returns a boolean value indicating whether the operation succeeded.
     *
     * Emits a {Transfer} event.
     */
    function transfer(address to, uint256 value) external returns (bool);

    /**
     * @dev Returns the remaining number of tokens that `spender` will be
     * allowed to spend on behalf of `owner` through {transferFrom}. This is
     * zero by default.
     *
     * This value changes when {approve} or {transferFrom} are called.
     */
    function allowance(address owner, address spender) external view returns (uint256);

    /**
     * @dev Sets a `value` amount of tokens as the allowance of `spender` over the
     * caller's tokens.
     *
     * Returns a boolean value indicating whether the operation succeeded.
     *
     * IMPORTANT: Beware that changing an allowance with this method brings the risk
     * that someone may use both the old and the new allowance by unfortunate
     * transaction ordering. One possible solution to mitigate this race
     * condition is to first reduce the spender's allowance to 0 and set the
     * desired value afterwards:
     * https://github.com/ethereum/EIPs/issues/20#issuecomment-263524729
     *
     * Emits an {Approval} event.
     */
    function approve(address spender, uint256 value) external returns (bool);

    /**
     * @dev Moves a `value` amount of tokens from `from` to `to` using the
     * allowance mechanism. `value` is then deducted from the caller's
     * allowance.
     *
     * Returns a boolean value indicating whether the operation succeeded.
     *
     * Emits a {Transfer} event.
     */
    function transferFrom(address from, address to, uint256 value) external returns (bool);
}


// ===== contracts/lib/Helper.sol =====
// SPDX-License-Identifier: UNLICENSED
pragma solidity ^0.8.20;

library Helper {
    function isContract(address account) internal view returns (bool) {
        uint256 size;
        assembly {
            size := extcodesize(account)
        }
        return size > 0;
    }

    function getAmountOut(uint256 amountIn, uint256 reserveIn, uint256 reserveOut)
        internal
        pure
        returns (uint256 amountOut)
    {
        uint256 amountInWithFee = amountIn * 9975;
        uint256 numerator = amountInWithFee * reserveOut;
        uint256 denominator = (reserveIn * 10000) + amountInWithFee;
        amountOut = numerator / denominator;
    }

    // given an output amount of an asset and pair reserves, returns a required input amount of the other asset
    function getAmountIn(uint256 amountOut, uint256 reserveIn, uint256 reserveOut)
        internal
        pure
        returns (uint256 amountIn)
    {
        uint256 numerator = reserveIn * amountOut * 10000;
        uint256 denominator = (reserveOut - amountOut) * 9975;
        amountIn = (numerator / denominator) + 1;
    }
}

// ===== contracts/abstract/ExcludedFromFeeList.sol =====
// SPDX-License-Identifier: MIT
pragma solidity >=0.8.17;

import {Owned} from "solmate/src/auth/Owned.sol";

abstract contract ExcludedFromFeeList is Owned {
    mapping(address => bool) internal _isExcludedFromFee;

    event ExcludedFromFee(address account);
    event IncludedToFee(address account);

    function isExcludedFromFee(address account) public view returns (bool) {
        return _isExcludedFromFee[account];
    }

    function excludeFromFee(address account) public onlyOwner {
        _isExcludedFromFee[account] = true;
        emit ExcludedFromFee(account);
    }

    function includeInFee(address account) public onlyOwner {
        _isExcludedFromFee[account] = false;
        emit IncludedToFee(account);
    }

    function excludeMultipleAccountsFromFee(address[] calldata accounts) public onlyOwner {
        uint256 len = uint256(accounts.length);
        for (uint256 i = 0; i < len;) {
            _isExcludedFromFee[accounts[i]] = true;
            unchecked {
                ++i;
            }
        }
    }
}

// ===== contracts/abstract/dex/BaseUSDT.sol =====
// SPDX-License-Identifier: MIT
pragma solidity ^0.8.4;

import {IUniswapV2Factory} from "@uniswap/v2-core/contracts/interfaces/IUniswapV2Factory.sol";
import {IUniswapV2Router02} from "@uniswap/v2-periphery/contracts/interfaces/IUniswapV2Router02.sol";
import {IERC20} from "@openzeppelin/contracts/token/ERC20/IERC20.sol";
import {_USDT, _ROUTER} from "../../Const.sol";

address constant PinkLock02 = 0x407993575c91ce7643a4d4cCACc9A98c36eE1BBE;
address constant USDT = _USDT;

contract Distributor {
    constructor() {
        IERC20(_USDT).approve(msg.sender, type(uint256).max);
    }
}

abstract contract BaseUSDT {
    bool public inSwapAndLiquify;
    IUniswapV2Router02 constant uniswapV2Router = IUniswapV2Router02(_ROUTER);
    address public immutable uniswapV2Pair;
    Distributor public immutable distributor;

    modifier lockTheSwap() {
        inSwapAndLiquify = true;
        _;
        inSwapAndLiquify = false;
    }

    constructor() {
        uniswapV2Pair = IUniswapV2Factory(uniswapV2Router.factory()).createPair(address(this), USDT);
        distributor = new Distributor();
    }
}

// ===== contracts/Const.sol =====
// SPDX-License-Identifier: UNLICENSED
pragma solidity ^0.8.20;

address constant _USDT = 0x55d398326f99059fF775485246999027B3197955;
address constant _ROUTER = 0x10ED43C718714eb63d5aA57B78B54704E256024E;

// ===== @uniswap/v2-periphery/contracts/interfaces/IUniswapV2Router02.sol =====
pragma solidity >=0.6.2;

import './IUniswapV2Router01.sol';

interface IUniswapV2Router02 is IUniswapV2Router01 {
    function removeLiquidityETHSupportingFeeOnTransferTokens(
        address token,
        uint liquidity,
        uint amountTokenMin,
        uint amountETHMin,
        address to,
        uint deadline
    ) external returns (uint amountETH);
    function removeLiquidityETHWithPermitSupportingFeeOnTransferTokens(
        address token,
        uint liquidity,
        uint amountTokenMin,
        uint amountETHMin,
        address to,
        uint deadline,
        bool approveMax, uint8 v, bytes32 r, bytes32 s
    ) external returns (uint amountETH);

    function swapExactTokensForTokensSupportingFeeOnTransferTokens(
        uint amountIn,
        uint amountOutMin,
        address[] calldata path,
        address to,
        uint deadline
    ) external;
    function swapExactETHForTokensSupportingFeeOnTransferTokens(
        uint amountOutMin,
        address[] calldata path,
        address to,
        uint deadline
    ) external payable;
    function swapExactTokensForETHSupportingFeeOnTransferTokens(
        uint amountIn,
        uint amountOutMin,
        address[] calldata path,
        address to,
        uint deadline
    ) external;
}


// ===== @uniswap/v2-core/contracts/interfaces/IUniswapV2Factory.sol =====
pragma solidity >=0.5.0;

interface IUniswapV2Factory {
    event PairCreated(address indexed token0, address indexed token1, address pair, uint);

    function feeTo() external view returns (address);
    function feeToSetter() external view returns (address);

    function getPair(address tokenA, address tokenB) external view returns (address pair);
    function allPairs(uint) external view returns (address pair);
    function allPairsLength() external view returns (uint);

    function createPair(address tokenA, address tokenB) external returns (address pair);

    function setFeeTo(address) external;
    function setFeeToSetter(address) external;
}


// ===== @uniswap/v2-periphery/contracts/interfaces/IUniswapV2Router01.sol =====
pragma solidity >=0.6.2;

interface IUniswapV2Router01 {
    function factory() external pure returns (address);
    function WETH() external pure returns (address);

    function addLiquidity(
        address tokenA,
        address tokenB,
        uint amountADesired,
        uint amountBDesired,
        uint amountAMin,
        uint amountBMin,
        address to,
        uint deadline
    ) external returns (uint amountA, uint amountB, uint liquidity);
    function addLiquidityETH(
        address token,
        uint amountTokenDesired,
        uint amountTokenMin,
        uint amountETHMin,
        address to,
        uint deadline
    ) external payable returns (uint amountToken, uint amountETH, uint liquidity);
    function removeLiquidity(
        address tokenA,
        address tokenB,
        uint liquidity,
        uint amountAMin,
        uint amountBMin,
        address to,
        uint deadline
    ) external returns (uint amountA, uint amountB);
    function removeLiquidityETH(
        address token,
        uint liquidity,
        uint amountTokenMin,
        uint amountETHMin,
        address to,
        uint deadline
    ) external returns (uint amountToken, uint amountETH);
    function removeLiquidityWithPermit(
        address tokenA,
        address tokenB,
        uint liquidity,
        uint amountAMin,
        uint amountBMin,
        address to,
        uint deadline,
        bool approveMax, uint8 v, bytes32 r, bytes32 s
    ) external returns (uint amountA, uint amountB);
    function removeLiquidityETHWithPermit(
        address token,
        uint liquidity,
        uint amountTokenMin,
        uint amountETHMin,
        address to,
        uint deadline,
        bool approveMax, uint8 v, bytes32 r, bytes32 s
    ) external returns (uint amountToken, uint amountETH);
    function swapExactTokensForTokens(
        uint amountIn,
        uint amountOutMin,
        address[] calldata path,
        address to,
        uint deadline
    ) external returns (uint[] memory amounts);
    function swapTokensForExactTokens(
        uint amountOut,
        uint amountInMax,
        address[] calldata path,
        address to,
        uint deadline
    ) external returns (uint[] memory amounts);
    function swapExactETHForTokens(uint amountOutMin, address[] calldata path, address to, uint deadline)
        external
        payable
        returns (uint[] memory amounts);
    function swapTokensForExactETH(uint amountOut, uint amountInMax, address[] calldata path, address to, uint deadline)
        external
        returns (uint[] memory amounts);
    function swapExactTokensForETH(uint amountIn, uint amountOutMin, address[] calldata path, address to, uint deadline)
        external
        returns (uint[] memory amounts);
    function swapETHForExactTokens(uint amountOut, address[] calldata path, address to, uint deadline)
        external
        payable
        returns (uint[] memory amounts);

    function quote(uint amountA, uint reserveA, uint reserveB) external pure returns (uint amountB);
    function getAmountOut(uint amountIn, uint reserveIn, uint reserveOut) external pure returns (uint amountOut);
    function getAmountIn(uint amountOut, uint reserveIn, uint reserveOut) external pure returns (uint amountIn);
    function getAmountsOut(uint amountIn, address[] calldata path) external view returns (uint[] memory amounts);
    function getAmountsIn(uint amountOut, address[] calldata path) external view returns (uint[] memory amounts);
}
